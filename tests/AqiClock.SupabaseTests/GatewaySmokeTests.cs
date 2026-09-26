using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Infrastructure.Supabase;
using AqiClock.Domain.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class GatewaySmokeTests(SupabaseFixture fixture)
{
    [SupabaseFact]
    public async Task OrganizationDateUsesItsTimezoneRatherThanThePcTimezone()
    {
        try
        {
            await fixture.SqlAsync("update public.organizations set timezone='Pacific/Kiritimati' where id=$1", SupabaseFixture.OrgAId);
            DateOnly expected = DateOnly.FromDateTime(await fixture.SqlScalarAsync<DateTime>(
                "select timezone('Pacific/Kiritimati', now())::date"));
            using SupabaseGateway gateway = CreateGateway();
            await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
            Assert.Equal(expected, await gateway.GetCurrentOrganizationDateAsync());
        }
        finally
        {
            await fixture.SqlAsync("update public.organizations set timezone='Europe/London' where id=$1", SupabaseFixture.OrgAId);
        }
    }

    [SupabaseFact]
    public async Task PasswordResetRequestUsesTheRealAuthEndpoint()
    {
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SendPasswordResetAsync(SupabaseFixture.Email("staff1"));
    }

    [SupabaseFact]
    public async Task WrongPasswordAndRevokedRefreshTokenAreClassifiedSeparately()
    {
        using SupabaseGateway gateway = CreateGateway();

        await Assert.ThrowsAsync<CredentialRejectedException>(() =>
            gateway.SignInAsync(SupabaseFixture.Email("staff1"), "wrong-password"));
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() =>
            gateway.RefreshSessionAsync(new StoredSession("unused", "missing-refresh-token", DateTimeOffset.UtcNow)));
    }

    [SupabaseFact]
    public async Task SignInPullWriteAndRepullUseTheRealDataApi()
    {
        using SupabaseGateway gateway = CreateGateway();
        AuthenticatedSession session = await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
        Assert.Equal(fixture.AdminUserId, session.UserId);

        foreach (CacheTable table in Enum.GetValues<CacheTable>())
        {
            CacheSnapshot snapshot = await gateway.PullAsync(table);
            Assert.NotNull(snapshot.Rows);
        }

        Guid id = Guid.NewGuid();
        var row = new TimetableRow(id, SupabaseFixture.OrgAId, $"Gateway smoke {fixture.RunId}", false);
        var realtimeSignal = new TaskCompletionSource<CacheTable>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using IRealtimeSubscription subscription = await gateway.SubscribeAsync((signal, _) =>
        {
            if (signal.Table == CacheTable.Timetables) realtimeSignal.TrySetResult(signal.Table);
            return Task.CompletedTask;
        });
        await gateway.InsertAsync(CacheTable.Timetables, row);
        Assert.Equal(CacheTable.Timetables, await realtimeSignal.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        CacheSnapshot afterInsert = await gateway.PullAsync(CacheTable.Timetables);
        Assert.Contains(afterInsert.Rows.Cast<TimetableRow>(), item => item.Id == id);

        await gateway.UpdateAsync(CacheTable.Timetables, id, row with { Name = $"Gateway updated {fixture.RunId}" });
        CacheSnapshot afterUpdate = await gateway.PullAsync(CacheTable.Timetables);
        Assert.Contains(afterUpdate.Rows.Cast<TimetableRow>(), item => item.Id == id && item.Name.StartsWith("Gateway updated", StringComparison.Ordinal));

        await gateway.DeleteAsync(CacheTable.Timetables, id);
        CacheSnapshot afterDelete = await gateway.PullAsync(CacheTable.Timetables);
        Assert.DoesNotContain(afterDelete.Rows.Cast<TimetableRow>(), item => item.Id == id);
    }

    [SupabaseFact]
    public async Task AdminCanUpdateProfileAndReadAuditThroughGateway()
    {
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);

        await gateway.UpdateProfileAsync(fixture.StaffUserId, "admin", true);
        CacheSnapshot promoted = await gateway.PullAsync(CacheTable.Profiles);
        Assert.Contains(promoted.Rows.Cast<ProfileRow>(), item => item.Id == fixture.StaffUserId && item.Role == "admin");

        await gateway.UpdateProfileAsync(fixture.StaffUserId, "teacher", true);
        IReadOnlyList<AuditEntry> audit = await gateway.GetAuditEntriesAsync();
        Assert.Contains(audit, item => item.EntityType == "profiles" && item.EntityId == fixture.StaffUserId && item.Action == "update");
    }

    [SupabaseFact]
    public async Task AdminCanUpdateWeekScheduleByWeekdayWithoutCachedServerId()
    {
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
        WeekScheduleRow monday = (await gateway.PullAsync(CacheTable.WeekSchedule)).Rows.Cast<WeekScheduleRow>().Single(x => x.Weekday == 0);

        await gateway.SaveWeekScheduleRowAsync(0, null, null);
        Assert.Null((await gateway.PullAsync(CacheTable.WeekSchedule)).Rows.Cast<WeekScheduleRow>().Single(x => x.Weekday == 0).TimetableId);

        await gateway.SaveWeekScheduleRowAsync(0, null, monday.TimetableId);
    }

    [SupabaseFact]
    public async Task AtomicTimetableSaveReordersPeriodsAndSwapsNames()
    {
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
        Guid timetableId = Guid.NewGuid();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        var timetable = new TimetableRow(timetableId, SupabaseFixture.OrgAId, $"Atomic {fixture.RunId}", false);
        PeriodRow first = new(firstId, timetableId, "Alpha", new(9, 0), new(10, 0), 0, true);
        PeriodRow second = new(secondId, timetableId, "Beta", new(10, 0), new(11, 0), 1, true);
        await gateway.SaveTimetableAsync(timetable, [first, second]);

        await gateway.SaveTimetableAsync(timetable, [second with { Name = "Alpha", SortOrder = 0 }, first with { Name = "Beta", SortOrder = 1 }]);

        PeriodRow[] saved = (await gateway.PullAsync(CacheTable.Periods)).Rows.Cast<PeriodRow>()
            .Where(x => x.TimetableId == timetableId).OrderBy(x => x.SortOrder).ToArray();
        Assert.Equal([secondId, firstId], saved.Select(x => x.Id));
        Assert.Equal(["Alpha", "Beta"], saved.Select(x => x.Name));
        await gateway.DeleteAsync(CacheTable.Timetables, timetableId);
    }

    [SupabaseFact]
    public async Task CommitTimeDuplicatePeriodNameMapsToDuplicateRowException()
    {
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
        Guid timetableId = Guid.NewGuid();
        var timetable = new TimetableRow(timetableId, SupabaseFixture.OrgAId, $"Duplicate {fixture.RunId}", false);
        PeriodRow first = new(Guid.NewGuid(), timetableId, "Repeated", new(9, 0), new(10, 0), 0, true);
        PeriodRow second = new(Guid.NewGuid(), timetableId, "Repeated", new(10, 0), new(11, 0), 1, true);

        DuplicateRowException error = await Assert.ThrowsAsync<DuplicateRowException>(
            () => gateway.SaveTimetableAsync(timetable, [first, second]));

        Assert.Equal("23505", error.ServerCode);
        Assert.DoesNotContain(
            (await gateway.PullAsync(CacheTable.Timetables)).Rows.Cast<TimetableRow>(),
            row => row.Id == timetableId);
    }

    [SupabaseFact]
    public async Task NonAdminCannotSaveTimetable()
    {
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SignInAsync(SupabaseFixture.Email("staff1"), SupabaseFixture.Password);
        var timetable = new TimetableRow(Guid.NewGuid(), SupabaseFixture.OrgAId, $"Denied {fixture.RunId}", false);

        await Assert.ThrowsAsync<ServerDeniedException>(() => gateway.SaveTimetableAsync(timetable, []));
    }

    [SupabaseFact]
    public async Task NonAdminCannotSaveWeekSchedule()
    {
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SignInAsync(SupabaseFixture.Email("staff1"), SupabaseFixture.Password);

        await Assert.ThrowsAsync<ServerDeniedException>(
            () => gateway.SaveWeekScheduleRowAsync(0, null, SupabaseFixture.SeedTimetableId));
    }

    [SupabaseFact]
    public async Task WeekScheduleSaveRejectsTimetableFromAnotherOrganization()
    {
        Guid foreignTimetableId = Guid.NewGuid();
        await fixture.SqlAsync(
            "insert into public.timetables (id, org_id, name) values ($1, $2, $3)",
            foreignTimetableId, fixture.OrgBId, $"Foreign week schedule {fixture.RunId}");
        try
        {
            using SupabaseGateway gateway = CreateGateway();
            await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);

            await Assert.ThrowsAsync<ServerDeniedException>(
                () => gateway.SaveWeekScheduleRowAsync(0, null, foreignTimetableId));
        }
        finally
        {
            await fixture.SqlAsync("delete from public.timetables where id = $1", foreignTimetableId);
        }
    }

    [SupabaseFact]
    public async Task AtomicTimetableSaveRejectsPeriodFromAnotherOrganization()
    {
        Guid foreignTimetableId = Guid.NewGuid();
        Guid foreignPeriodId = Guid.NewGuid();
        await fixture.SqlAsync("insert into public.timetables (id, org_id, name) values ($1, $2, $3)", foreignTimetableId, fixture.OrgBId, $"Foreign {fixture.RunId}");
        await fixture.SqlAsync("insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order) values ($1, $2, $3, '09:00', '10:00', 0)", foreignPeriodId, foreignTimetableId, $"Foreign period {fixture.RunId}");
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
        Guid ownTimetableId = Guid.NewGuid();
        var timetable = new TimetableRow(ownTimetableId, SupabaseFixture.OrgAId, $"Own {fixture.RunId}", false);
        var period = new PeriodRow(foreignPeriodId, ownTimetableId, "Hijack", new(9, 0), new(10, 0), 0, true);

        await Assert.ThrowsAsync<ServerDeniedException>(() => gateway.SaveTimetableAsync(timetable, [period]));

        await fixture.SqlAsync("delete from public.timetables where id = $1", foreignTimetableId);
    }

    [SupabaseFact]
    public async Task WeekScheduleUpsertCreatesMissingWeekday()
    {
        await fixture.SqlAsync("delete from public.week_schedule where org_id = $1 and weekday = 0", SupabaseFixture.OrgAId);
        try
        {
            using SupabaseGateway gateway = CreateGateway();
            await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
            await gateway.SaveWeekScheduleRowAsync(0, null, SupabaseFixture.SeedTimetableId);
            WeekScheduleRow monday = (await gateway.PullAsync(CacheTable.WeekSchedule)).Rows.Cast<WeekScheduleRow>().Single(x => x.Weekday == 0);
            Assert.Equal(SupabaseFixture.SeedTimetableId, monday.TimetableId);
        }
        finally
        {
            await fixture.SqlAsync("delete from public.week_schedule where org_id = $1 and weekday = 0", SupabaseFixture.OrgAId);
            await fixture.SqlAsync("insert into public.week_schedule (id, org_id, weekday, timetable_id) values ($1, $2, 0, null)", SupabaseFixture.SeedWeekdayMondayId, SupabaseFixture.OrgAId);
        }
    }

    [SupabaseFact]
    public async Task DuplicateDefaultWeekdayIsRejectedByNullsNotDistinctConstraint()
    {
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() => fixture.SqlAsync(
            "insert into public.week_schedule(org_id,weekday,audience_class_id,timetable_id) values ($1,0,null,null)",
            SupabaseFixture.OrgAId));

        Assert.Equal("23505", error.SqlState);
        Assert.Equal("week_schedule_org_weekday_audience_key", error.ConstraintName);
    }

    [SupabaseFact]
    public async Task LegacyTwoArgumentWeekScheduleRpcWritesClearsAndUpdatesDefaultRow()
    {
        const int weekday = 6;
        Guid? originalTimetableId = await fixture.SqlScalarAsync<Guid?>(
            "select timetable_id from public.week_schedule where org_id=$1 and weekday=$2 and audience_class_id is null",
            SupabaseFixture.OrgAId, weekday);
        try
        {
            await CallLegacyWeekScheduleRpcAsync(fixture.AdminUserId, weekday, SupabaseFixture.SeedTimetableId);
            Assert.Equal(SupabaseFixture.SeedTimetableId, await fixture.SqlScalarAsync<Guid?>(
                "select timetable_id from public.week_schedule where org_id=$1 and weekday=$2 and audience_class_id is null",
                SupabaseFixture.OrgAId, weekday));
            Assert.True(await fixture.SqlScalarAsync<bool>(
                "select audience_class_id is null from public.week_schedule where org_id=$1 and weekday=$2",
                SupabaseFixture.OrgAId, weekday));

            await CallLegacyWeekScheduleRpcAsync(fixture.AdminUserId, weekday, null);
            Assert.Null(await fixture.SqlScalarAsync<Guid?>(
                "select timetable_id from public.week_schedule where org_id=$1 and weekday=$2 and audience_class_id is null",
                SupabaseFixture.OrgAId, weekday));

            await CallLegacyWeekScheduleRpcAsync(fixture.AdminUserId, weekday, SupabaseFixture.SeedTimetableId);
            Assert.Equal(1L, await fixture.SqlScalarAsync<long>(
                "select count(*) from public.week_schedule where org_id=$1 and weekday=$2 and audience_class_id is null",
                SupabaseFixture.OrgAId, weekday));
        }
        finally
        {
            await fixture.SqlAsync(
                "update public.week_schedule set timetable_id=$1 where org_id=$2 and weekday=$3 and audience_class_id is null",
                originalTimetableId is { } id ? id : DBNull.Value, SupabaseFixture.OrgAId, weekday);
        }
    }

    [SupabaseFact]
    public async Task LegacyTwoArgumentWeekScheduleRpcRejectsNonAdmin()
    {
        PostgresException error = await Assert.ThrowsAsync<PostgresException>(
            () => CallLegacyWeekScheduleRpcAsync(fixture.StaffUserId, 6, SupabaseFixture.SeedTimetableId));

        Assert.Equal("42501", error.SqlState);
    }

    [SupabaseFact]
    public async Task WeekScheduleRpcsEnforceAdminOwnershipAndDefaultDeleteGuard()
    {
        Guid foreignClassId = Guid.NewGuid();
        await fixture.SqlAsync("insert into public.classes(id,org_id,name,sort_order) values($1,$2,$3,9001)", foreignClassId, fixture.OrgBId, $"Foreign track {fixture.RunId}");
        try
        {
            using SupabaseGateway admin = CreateGateway();
            await admin.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
            await Assert.ThrowsAsync<ServerDeniedException>(() => admin.SaveWeekScheduleRowAsync(0, foreignClassId, SupabaseFixture.SeedTimetableId));

            using SupabaseGateway staff = CreateGateway();
            await staff.SignInAsync(SupabaseFixture.Email("staff1"), SupabaseFixture.Password);
            await Assert.ThrowsAsync<ServerDeniedException>(() => staff.SaveWeekScheduleRowAsync(0, null, SupabaseFixture.SeedTimetableId));

            using HttpResponseMessage deleteDefault = await fixture.RestAsync(TestPersona.Admin, HttpMethod.Post, "rpc/admin_delete_week_schedule",
                new System.Text.Json.Nodes.JsonObject { ["p_weekday"] = 0, ["p_audience_class_id"] = null });
            Assert.False(deleteDefault.IsSuccessStatusCode);
            Assert.Contains("default", await deleteDefault.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }
        finally { await fixture.SqlAsync("delete from public.classes where id=$1", foreignClassId); }
    }

    [SupabaseFact]
    public async Task WeekScheduleRlsRejectsCrossOrgClassOnInsertAndUpdate()
    {
        Guid ownClassId = Guid.NewGuid(), foreignClassId = Guid.NewGuid();
        await fixture.SqlAsync("insert into public.classes(id,org_id,name,sort_order) values($1,$2,$3,9002),($4,$5,$6,9003)", ownClassId, SupabaseFixture.OrgAId, $"Own RLS {fixture.RunId}", foreignClassId, fixture.OrgBId, $"Foreign RLS {fixture.RunId}");
        try
        {
            var insert = new System.Text.Json.Nodes.JsonObject { ["org_id"] = SupabaseFixture.OrgAId, ["weekday"] = 1, ["audience_class_id"] = foreignClassId };
            using HttpResponseMessage deniedInsert = await fixture.RestAsync(TestPersona.Admin, HttpMethod.Post, "week_schedule", insert);
            Assert.False(deniedInsert.IsSuccessStatusCode);

            using SupabaseGateway gateway = CreateGateway();
            await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
            await gateway.SaveWeekScheduleRowAsync(1, ownClassId, null);
            var update = new System.Text.Json.Nodes.JsonObject { ["audience_class_id"] = foreignClassId };
            using HttpResponseMessage deniedUpdate = await fixture.RestAsync(TestPersona.Admin, HttpMethod.Patch, $"week_schedule?org_id=eq.{SupabaseFixture.OrgAId}&weekday=eq.1&audience_class_id=eq.{ownClassId}", update);
            Assert.False(deniedUpdate.IsSuccessStatusCode);
        }
        finally
        {
            await fixture.SqlAsync("delete from public.week_schedule where audience_class_id=$1", ownClassId);
            await fixture.SqlAsync("delete from public.classes where id=$1 or id=$2", ownClassId, foreignClassId);
        }
    }

    [SupabaseFact]
    public async Task TrackRowRoundTripsDeletesAndPreventsReferencedClassDeletion()
    {
        Guid classId = Guid.NewGuid();
        await fixture.SqlAsync("insert into public.classes(id,org_id,name,sort_order) values($1,$2,$3,9004)", classId, SupabaseFixture.OrgAId, $"Track {fixture.RunId}");
        using SupabaseGateway gateway = CreateGateway();
        await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
        try
        {
            await gateway.SaveWeekScheduleRowAsync(2, classId, SupabaseFixture.SeedTimetableId);
            WeekScheduleRow row = (await gateway.PullAsync(CacheTable.WeekSchedule)).Rows.Cast<WeekScheduleRow>().Single(item => item.Weekday == 2 && item.AudienceClassId == classId);
            Assert.Equal(SupabaseFixture.SeedTimetableId, row.TimetableId);
            await Assert.ThrowsAsync<ReferencedRowException>(() => gateway.DeleteAsync(CacheTable.Classes, classId));

            using HttpResponseMessage studentRead = await fixture.RestAsync(TestPersona.StudentDevice, HttpMethod.Get, "week_schedule?select=id,weekday,audience_class_id,timetable_id");
            System.Text.Json.Nodes.JsonArray studentRows = Assert.IsType<System.Text.Json.Nodes.JsonArray>(await SupabaseFixture.RowsAsync(studentRead));
            Assert.Contains(studentRows, item => item?["audience_class_id"]?.GetValue<Guid>() == classId);

            await gateway.DeleteWeekScheduleRowAsync(2, classId);
            Assert.DoesNotContain((await gateway.PullAsync(CacheTable.WeekSchedule)).Rows.Cast<WeekScheduleRow>(), item => item.AudienceClassId == classId);
        }
        finally
        {
            await fixture.SqlAsync("delete from public.week_schedule where audience_class_id=$1", classId);
            await fixture.SqlAsync("delete from public.classes where id=$1", classId);
        }
    }

    [SupabaseFact]
    public async Task SimplifiedGeneratorPreviewSaveRestoreAndPrayerWritesRoundTrip()
    {
        Guid timetableId = Guid.NewGuid();
        Guid firstPeriodId = Guid.NewGuid();
        Guid secondPeriodId = Guid.NewGuid();
        DateOnly targetDate = DateOnly.FromDateTime(await fixture.SqlScalarAsync<DateTime>(
            "select timezone('Europe/London', now())::date"));
        DateOnly standingDate = targetDate.AddDays(10);
        await fixture.SqlAsync("insert into public.timetables(id,org_id,name) values($1,$2,$3)", timetableId, SupabaseFixture.OrgAId, $"Gateway generator {timetableId:N}");
        await fixture.SqlAsync("insert into public.periods(id,timetable_id,name,start_time,end_time,sort_order,is_lesson) values($1,$3,'Original 1','09:00','09:30',0,true),($2,$3,'Original break','09:30','09:40',1,false)", firstPeriodId, secondPeriodId, timetableId);
        try
        {
            using SupabaseGateway gateway = CreateGateway();
            await gateway.SignInAsync(SupabaseFixture.Email("admin1"), SupabaseFixture.Password);
            Assert.Null(await gateway.GetTimetableShapeAsync(timetableId));
            PrayerTimesSnapshot prayers = await gateway.GetPrayerTimesAsync(targetDate);
            Guid maghrib = prayers.Anchors.Single(x => x.Key == "maghrib").Id;
            DateOnly bulkDate = targetDate.AddYears(5);
            var shape = new TimetableShape(new(9, 5), 3, 20, null, null, false);
            GeneratorResult expected = TimetableGenerator.Expand(timetableId, shape, []);
            PeriodRow[] expectedRows = expected.Periods.Select((period, index) =>
                new PeriodRow(period.Id, timetableId, period.Name, period.Start, period.End, index, period.IsLesson)).ToArray();
            long auditBefore = await fixture.SqlScalarAsync<long>("select count(*) from public.audit_log");
            GeneratorServerPreview serverPreview = await gateway.PreviewGeneratedTimetableAsync(
                timetableId, shape, targetDate);
            Assert.Equal(targetDate, serverPreview.Date);
            Assert.Equal(expectedRows, serverPreview.Periods);
            Assert.Equal(0, await fixture.SqlScalarAsync<int>(
                "select count(*)::integer from public.timetable_generators where timetable_id=$1", timetableId));
            Assert.Equal(auditBefore, await fixture.SqlScalarAsync<long>("select count(*) from public.audit_log"));

            IReadOnlyList<PeriodRow> saved = await gateway.SaveGeneratedTimetableAsync(timetableId, shape);
            Assert.Equal(expectedRows, saved);
            Assert.Equal(shape, await gateway.GetTimetableShapeAsync(timetableId));

            int bulkWritten = await gateway.BulkUpsertAnchorDateOverridesAsync(maghrib,
                [new(bulkDate, new(18, 42), 10)]);
            DateOnly rejectedDate = bulkDate.AddDays(1);
            await Assert.ThrowsAnyAsync<ServerWriteException>(() => gateway.BulkUpsertAnchorDateOverridesAsync(maghrib,
                [new(rejectedDate, new(18, 41), 10), new(rejectedDate.AddDays(1), new(18, 40), 0)]));
            Assert.Equal(1, bulkWritten);

            int fixedWritten = await gateway.SavePrayerFixedTimesAsync(
            [
                new("asr", new(17, 0), 10), new("isha", new(20, 15), 10),
                new("zuhr", new(13, 0), null, 4)
            ], standingDate);
            Assert.Equal(3, fixedWritten);
            Assert.True(await fixture.SqlScalarAsync<bool>(
                """
                select standing.duration_minutes is null
                from public.anchor_standing_times standing
                join public.organization_anchors anchor on anchor.id = standing.anchor_id
                where standing.org_id=$1 and anchor.key='zuhr'
                  and standing.weekday=4 and standing.effective_from=$2
                """, SupabaseFixture.OrgAId, standingDate));
            Assert.Equal(2, await fixture.SqlScalarAsync<int>(
                """
                select count(*)::integer
                from public.anchor_standing_times standing
                join public.organization_anchors anchor on anchor.id = standing.anchor_id
                where standing.org_id=$1 and anchor.key in ('asr','isha')
                  and standing.weekday is null and standing.duration_minutes is not null
                  and standing.effective_from=$2
                """, SupabaseFixture.OrgAId, standingDate));
            await Assert.ThrowsAnyAsync<ServerWriteException>(() => gateway.SavePrayerFixedTimesAsync(
            [
                new("asr", new(17, 0), 10, 1), new("isha", new(20, 15), 10),
                new("zuhr", new(13, 0), null, 4)
            ], standingDate));

            IReadOnlyList<PeriodRow> restored = await gateway.DisableGeneratedTimetableAsync(timetableId);
            Assert.Equal([firstPeriodId, secondPeriodId], restored.Select(item => item.Id));
            Assert.Null(await gateway.GetTimetableShapeAsync(timetableId));
            Assert.Contains((await gateway.GetPrayerTimesAsync(bulkDate)).DateOverrides,
                item => item.AnchorId == maghrib && item.Date == bulkDate && item.StartTime == new TimeOnly(18, 42));
            Assert.DoesNotContain((await gateway.GetPrayerTimesAsync(rejectedDate)).DateOverrides,
                item => item.AnchorId == maghrib && item.Date == rejectedDate);
        }
        finally
        {
            await fixture.SqlAsync("delete from public.anchor_date_overrides where org_id=$1 and date between $2 and $3", SupabaseFixture.OrgAId, targetDate.AddYears(5), targetDate.AddYears(5).AddDays(2));
            await fixture.SqlAsync("delete from public.anchor_standing_times where org_id=$1 and effective_from=$2", SupabaseFixture.OrgAId, standingDate);
            await fixture.SqlAsync("update public.timetables set is_generated=false where id=$1", timetableId);
            await fixture.SqlAsync("delete from public.timetables where id=$1", timetableId);
        }
    }

    private static SupabaseGateway CreateGateway()
    {
        var options = Options.Create(new SupabaseOptions
        {
            Url = SupabaseEnvironment.Url ?? throw new InvalidOperationException("SUPABASE_URL is required."),
            AnonKey = SupabaseEnvironment.AnonKey ?? throw new InvalidOperationException("SUPABASE_ANON_KEY is required."),
        });
        return new SupabaseGateway(options, NullLogger<SupabaseGateway>.Instance);
    }

    private Task CallLegacyWeekScheduleRpcAsync(Guid userId, int weekday, Guid? timetableId) => fixture.SqlAsync(
        """
        with role_set as materialized (
            select set_config('role', 'authenticated', true)
        ), subject_set as materialized (
            select set_config('request.jwt.claim.sub', $1::text, true) from role_set
        ), claim_role_set as materialized (
            select set_config('request.jwt.claim.role', 'authenticated', true) from subject_set
        )
        select public.admin_save_week_schedule($2::smallint, $3::uuid) from claim_role_set;
        """,
        userId, weekday, timetableId is { } id ? id : DBNull.Value);
}
