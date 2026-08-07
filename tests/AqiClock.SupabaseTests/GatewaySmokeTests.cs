using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Infrastructure.Supabase;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class GatewaySmokeTests(SupabaseFixture fixture)
{
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

    private static SupabaseGateway CreateGateway()
    {
        var options = Options.Create(new SupabaseOptions
        {
            Url = SupabaseEnvironment.Url ?? throw new InvalidOperationException("SUPABASE_URL is required."),
            AnonKey = SupabaseEnvironment.AnonKey ?? throw new InvalidOperationException("SUPABASE_ANON_KEY is required."),
        });
        return new SupabaseGateway(options, NullLogger<SupabaseGateway>.Instance);
    }
}
