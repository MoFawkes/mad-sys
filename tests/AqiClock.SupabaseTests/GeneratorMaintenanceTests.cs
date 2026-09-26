using System.Text.Json.Nodes;
using AqiClock.Domain.Scheduling;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class GeneratorMaintenanceTests(SupabaseFixture fixture)
{
    [SupabaseFact]
    public async Task SqlExpansionMatchesDomainFixtures()
    {
        Dictionary<string, Guid> anchors = await AnchorIdsAsync();
        ExpansionCase[] cases =
        [
            new("morning break", new(2090, 1, 9),
                new(new(9, 10), 8, 30, 4, 25, true), []),
            new("evening prayers and derived Naseehah", new(2090, 1, 10),
                new(new(18, 15), 5, 25, null, null, true),
                [new(anchors["asr"], "asr", "Asr", new(18, 40), 10),
                 new(anchors["maghrib"], "maghrib", "Maghrib", new(19, 30), 10),
                 new(anchors["isha"], "isha", "Isha", new(20, 30), 10)]),
            new("prayer adjustment disabled", new(2090, 1, 11),
                new(new(18, 15), 6, 25, null, null, false),
                [new(anchors["maghrib"], "maghrib", "Maghrib", new(19, 10), 10)]),
            new("production Part-Time 2", new(2090, 1, 12),
                new(new(17, 50), 6, 25, null, null, true),
                [new(anchors["maghrib"], "maghrib", "Maghrib", new(18, 55), 10)])
        ];

        foreach (ExpansionCase expansionCase in cases)
            await AssertSqlDomainParityAsync(expansionCase, anchors.Values);
    }

    [SupabaseFact]
    public async Task FridayJumuahUsesItsAuthoredDuration()
    {
        Guid timetableId = Guid.NewGuid();
        Guid zuhr = (await AnchorIdsAsync())["zuhr"];
        DateOnly friday = new(2090, 1, 6);
        Guid defaultId = Guid.NewGuid();
        Guid fridayId = Guid.NewGuid();
        try
        {
            await fixture.SqlAsync(
                "insert into public.anchor_standing_times(id,org_id,anchor_id,weekday,start_time,duration_minutes,effective_from) values($1,$3,$4,null,'12:45',10,'2089-01-01'),($2,$3,$4,4,'13:15',30,'2089-01-01')",
                defaultId, fridayId, SupabaseFixture.OrgAId, zuhr);

            Assert.Equal(1, await fixture.SqlScalarAsync<int>(
                "select count(*)::integer from unnest(private.expand_timetable_shape($1,$2,'12:30',4,30,null,null,true,$3)) expanded where expanded.name='Zuhr' and expanded.start_time='13:15' and expanded.end_time='13:45'",
                timetableId, SupabaseFixture.OrgAId, friday));
        }
        finally
        {
            await fixture.SqlAsync("delete from public.anchor_standing_times where id in ($1,$2)", defaultId, fridayId);
        }
    }

    [SupabaseFact]
    public async Task AnchorResolutionUsesNewestWeekdayThenDefaultAndHonoursCancellation()
    {
        Guid timetableId = Guid.NewGuid();
        Guid zuhr = (await AnchorIdsAsync())["zuhr"];
        Guid defaultStanding = Guid.NewGuid();
        Guid olderMonday = Guid.NewGuid();
        Guid newerMonday = Guid.NewGuid();
        Guid dateOverride = Guid.NewGuid();
        Guid cancellation = Guid.NewGuid();
        DateOnly monday = new(2035, 1, 8);
        DateOnly tuesday = monday.AddDays(1);
        DateOnly wednesday = monday.AddDays(2);
        DateOnly nextMonday = monday.AddDays(7);
        try
        {
            await fixture.SqlAsync(
                """
                insert into public.anchor_standing_times
                    (id,org_id,anchor_id,weekday,start_time,duration_minutes,effective_from)
                values ($1,$4,$5,null,'12:45',10,'2030-01-01'),
                       ($2,$4,$5,0,'12:50',10,'2034-01-01'),
                       ($3,$4,$5,0,'12:55',10,'2035-01-01')
                """, defaultStanding, olderMonday, newerMonday, SupabaseFixture.OrgAId, zuhr);
            await fixture.SqlAsync(
                """
                insert into public.anchor_date_overrides
                    (id,org_id,anchor_id,date,start_time,duration_minutes,is_cancelled)
                values ($1,$3,$4,$5,'13:05',10,false),
                       ($2,$3,$4,$6,null,null,true)
                """, dateOverride, cancellation, SupabaseFixture.OrgAId, zuhr, monday, wednesday);

            Assert.Equal(1, await ExpandedAnchorAtAsync(timetableId, monday, new(13, 5)));
            Assert.Equal(1, await ExpandedAnchorAtAsync(timetableId, nextMonday, new(12, 55)));
            Assert.Equal(1, await ExpandedAnchorAtAsync(timetableId, tuesday, new(12, 45)));
            Assert.Equal(0, await ExpandedAnchorCountAsync(timetableId, wednesday));
        }
        finally
        {
            await fixture.SqlAsync("delete from public.anchor_date_overrides where id in ($1,$2)", dateOverride, cancellation);
            await fixture.SqlAsync("delete from public.anchor_standing_times where id in ($1,$2,$3)", defaultStanding, olderMonday, newerMonday);
        }
    }

    [SupabaseFact]
    public async Task MaintenanceRepairsSortOrderAndThenBecomesNoOp()
    {
        Guid timetableId = Guid.NewGuid();
        DateOnly targetDate = DateOnly.FromDateTime(await fixture.SqlScalarAsync<DateTime>(
            "select timezone('Europe/London', now())::date"));
        try
        {
            await fixture.SqlAsync(
                "insert into public.timetables(id,org_id,name,is_generated) values($1,$2,$3,true)",
                timetableId, SupabaseFixture.OrgAId, $"Maintenance {timetableId:N}");
            await fixture.SqlAsync(
                "insert into public.timetable_generators(timetable_id,org_id,day_start,lesson_count,lesson_minutes,adjusts_for_prayer,pre_conversion_periods) values($1,$2,'09:00',3,20,false,'[]'::jsonb)",
                timetableId, SupabaseFixture.OrgAId);

            await RunScheduledAsync();
            await fixture.SqlAsync(
                "update public.periods set sort_order=sort_order+10 from (select set_config('aqi.generator_write','on',true)) configured where timetable_id=$1",
                timetableId);
            await RunScheduledAsync();

            Assert.Equal("0,1,2", await fixture.SqlScalarAsync<string>(
                "select string_agg(sort_order::text,',' order by sort_order) from public.periods where timetable_id=$1",
                timetableId));
            string before = await FingerprintAsync(timetableId);
            long auditBefore = await PeriodAuditCountAsync(timetableId);
            await RunScheduledAsync();
            Assert.Equal(before, await FingerprintAsync(timetableId));
            Assert.Equal(auditBefore, await PeriodAuditCountAsync(timetableId));
        }
        finally
        {
            await fixture.SqlAsync("delete from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2", SupabaseFixture.OrgAId, targetDate);
            await fixture.SqlAsync("select set_config('aqi.generator_write','on',true)");
            await fixture.SqlAsync("update public.timetables set is_generated=false where id=$1", timetableId);
            await fixture.SqlAsync("delete from public.timetables where id=$1", timetableId);
        }
    }

    [SupabaseFact]
    public async Task ConsecutiveDatesWriteOnlyTheTimetableWhoseExpansionChanged()
    {
        Guid amTimetable = Guid.NewGuid();
        Guid pmTimetable = Guid.NewGuid();
        Guid zuhrStanding = Guid.NewGuid();
        Guid asrStanding = Guid.NewGuid();
        Guid firstMaghrib = Guid.NewGuid();
        Guid secondMaghrib = Guid.NewGuid();
        Guid failingZuhr = Guid.NewGuid();
        Dictionary<string, Guid> anchors = await AnchorIdsAsync();
        DateOnly firstDate = DateOnly.FromDateTime(await fixture.SqlScalarAsync<DateTime>(
            "select timezone('Pacific/Honolulu', now())::date"));
        DateOnly secondDate = firstDate.AddDays(1);

        try
        {
            await fixture.SqlAsync(
                "insert into public.timetables(id,org_id,name,is_generated) values($1,$3,$4,true),($2,$3,$5,true)",
                amTimetable, pmTimetable, SupabaseFixture.OrgAId,
                $"Maintenance AM {amTimetable:N}", $"Maintenance PM {pmTimetable:N}");
            await fixture.SqlAsync(
                """
                insert into public.timetable_generators
                    (timetable_id,org_id,day_start,lesson_count,lesson_minutes,break_after_lesson,
                     break_minutes,adjusts_for_prayer,pre_conversion_periods)
                values ($1,$3,'09:10',8,30,4,25,true,'[]'::jsonb),
                       ($2,$3,'18:15',5,25,null,null,true,'[]'::jsonb)
                """, amTimetable, pmTimetable, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                """
                insert into public.anchor_standing_times
                    (id,org_id,anchor_id,start_time,duration_minutes,effective_from)
                values ($1,$3,$4,'13:37',10,$6),($2,$3,$5,'18:40',10,$6)
                """, zuhrStanding, asrStanding, SupabaseFixture.OrgAId,
                anchors["zuhr"], anchors["asr"], firstDate.AddDays(-30));
            await fixture.SqlAsync(
                """
                insert into public.anchor_date_overrides
                    (id,org_id,anchor_id,date,start_time,duration_minutes)
                values ($1,$3,$4,$5,'20:12',10),($2,$3,$4,$6,'20:10',10)
                """, firstMaghrib, secondMaghrib, SupabaseFixture.OrgAId,
                anchors["maghrib"], firstDate, secondDate);
            await fixture.SqlAsync("update public.organizations set timezone='Pacific/Honolulu' where id=$1", SupabaseFixture.OrgAId);

            await RunScheduledAsync();
            Assert.Equal(2, await WrittenForDateAsync(firstDate));
            string amBefore = await FingerprintAsync(amTimetable);
            string pmBefore = await FingerprintAsync(pmTimetable);
            long amAuditBefore = await PeriodAuditCountAsync(amTimetable);
            long pmAuditBefore = await PeriodAuditCountAsync(pmTimetable);

            await fixture.SqlAsync("update public.organizations set timezone='Pacific/Kiritimati' where id=$1", SupabaseFixture.OrgAId);
            await RunScheduledAsync();

            Assert.Equal(1, await WrittenForDateAsync(secondDate));
            Assert.Equal(amBefore, await FingerprintAsync(amTimetable));
            Assert.NotEqual(pmBefore, await FingerprintAsync(pmTimetable));
            Assert.Equal(amAuditBefore, await PeriodAuditCountAsync(amTimetable));
            Assert.True(await PeriodAuditCountAsync(pmTimetable) > pmAuditBefore);

            string pmAfterSecondDate = await FingerprintAsync(pmTimetable);
            long pmAuditAfterSecondDate = await PeriodAuditCountAsync(pmTimetable);
            await RunScheduledAsync();
            Assert.Equal(amAuditBefore, await PeriodAuditCountAsync(amTimetable));
            Assert.Equal(pmAuditAfterSecondDate, await PeriodAuditCountAsync(pmTimetable));

            await fixture.SqlAsync(
                "delete from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2",
                SupabaseFixture.OrgAId, secondDate);
            await fixture.SqlAsync(
                "insert into public.anchor_date_overrides(id,org_id,anchor_id,date,start_time,duration_minutes) values($1,$2,$3,$4,'13:20',null)",
                failingZuhr, SupabaseFixture.OrgAId, anchors["zuhr"], secondDate);
            await fixture.SqlAsync(
                "update public.anchor_date_overrides set start_time='20:08' where id=$1", secondMaghrib);
            await RunScheduledAsync();

            Assert.Equal(1, await WrittenForDateAsync(secondDate));
            Assert.Equal(amBefore, await FingerprintAsync(amTimetable));
            Assert.NotEqual(pmAfterSecondDate, await FingerprintAsync(pmTimetable));
            Assert.Equal(amAuditBefore, await PeriodAuditCountAsync(amTimetable));
            Assert.True(await PeriodAuditCountAsync(pmTimetable) > pmAuditAfterSecondDate);
            string error = (await fixture.SqlScalarAsync<string>(
                "select error from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2",
                SupabaseFixture.OrgAId, secondDate))!;
            Assert.Contains(amTimetable.ToString(), error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Zuhr", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.SqlAsync(
                "delete from public.generator_maintenance_runs where org_id=$1 and regenerated_date in ($2,$3)",
                SupabaseFixture.OrgAId, firstDate, secondDate);
            await fixture.SqlAsync("update public.timetables set is_generated=false where id in ($1,$2)", amTimetable, pmTimetable);
            await fixture.SqlAsync("delete from public.timetables where id in ($1,$2)", amTimetable, pmTimetable);
            await fixture.SqlAsync("delete from public.anchor_date_overrides where id in ($1,$2,$3)", firstMaghrib, secondMaghrib, failingZuhr);
            await fixture.SqlAsync("delete from public.anchor_standing_times where id in ($1,$2)", zuhrStanding, asrStanding);
            await fixture.SqlAsync("update public.organizations set timezone='Europe/London' where id=$1", SupabaseFixture.OrgAId);
        }
    }

    [SupabaseFact]
    public async Task EntryPointsRejectTheOtherCallersKey()
    {
        DateOnly targetDate = DateOnly.FromDateTime(await fixture.SqlScalarAsync<DateTime>(
            "select timezone('Europe/London', now())::date"));
        await fixture.SqlAsync(
            "delete from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2",
            SupabaseFixture.OrgAId, targetDate);
        try
        {
            using HttpResponseMessage staffScheduled = await fixture.RestAsync(
                TestPersona.Staff, HttpMethod.Post, "rpc/run_generator_maintenance",
                new JsonObject { ["p_org_id"] = SupabaseFixture.OrgAId.ToString() });
            Assert.False(staffScheduled.IsSuccessStatusCode);

            using HttpResponseMessage serviceAdmin = await fixture.ServiceRestAsync(
                HttpMethod.Post, "rpc/admin_regenerate_generated_timetables", new JsonObject());
            Assert.False(serviceAdmin.IsSuccessStatusCode);

            using HttpResponseMessage admin = await fixture.RestAsync(
                TestPersona.Admin, HttpMethod.Post, "rpc/admin_regenerate_generated_timetables", new JsonObject());
            Assert.True(admin.IsSuccessStatusCode, await admin.Content.ReadAsStringAsync());
        }
        finally
        {
            await fixture.SqlAsync(
                "delete from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2",
                SupabaseFixture.OrgAId, targetDate);
        }
    }

    [SupabaseFact]
    public async Task GeneratorGuidSortKeyMatchesDotNetGuidCompareTo()
    {
        Guid signedFirst = Guid.Parse("80000000-8000-8000-0000-000000000001");
        Guid positiveLast = Guid.Parse("7fffffff-7fff-7fff-0000-000000000002");
        Guid expected = new[] { positiveLast, signedFirst }.OrderBy(id => id).First();
        Guid actual = (await fixture.SqlScalarAsync<Guid?>(
            "select id from unnest(array[$1::uuid,$2::uuid]) id order by private.generator_guid_sort_key(id) limit 1",
            positiveLast, signedFirst))!.Value;
        Assert.Equal(expected, actual);
    }

    private async Task AssertSqlDomainParityAsync(ExpansionCase expansionCase, IEnumerable<Guid> allAnchorIds)
    {
        Guid timetableId = Guid.NewGuid();
        var overrideIds = new List<Guid>();
        try
        {
            foreach (Guid anchorId in allAnchorIds)
            {
                ResolvedAnchor? resolved = expansionCase.Anchors.FirstOrDefault(item => item.Id == anchorId);
                Guid overrideId = Guid.NewGuid();
                overrideIds.Add(overrideId);
                if (resolved is null)
                    await fixture.SqlAsync(
                        "insert into public.anchor_date_overrides(id,org_id,anchor_id,date,is_cancelled) values($1,$2,$3,$4,true)",
                        overrideId, SupabaseFixture.OrgAId, anchorId, expansionCase.Date);
                else
                    await fixture.SqlAsync(
                        "insert into public.anchor_date_overrides(id,org_id,anchor_id,date,start_time,duration_minutes) values($1,$2,$3,$4,$5,$6)",
                        overrideId, SupabaseFixture.OrgAId, anchorId, expansionCase.Date,
                        resolved.Start, resolved.DurationMinutes ?? 10);
            }

            GeneratorResult expected = TimetableGenerator.Expand(timetableId, expansionCase.Shape, expansionCase.Anchors);
            Assert.Equal(expected.Periods.Count, await fixture.SqlScalarAsync<int>(
                "select cardinality(private.expand_timetable_shape($1,$2,$3,$4,$5,$6,$7,$8,$9))",
                ShapeArguments(timetableId, expansionCase)));
            for (int index = 0; index < expected.Periods.Count; index++)
            {
                GeneratedPeriod period = expected.Periods[index];
                object[] args = [.. ShapeArguments(timetableId, expansionCase), index + 1L,
                    period.Id, period.Name, period.Start, period.End, period.IsLesson];
                Assert.Equal(1, await fixture.SqlScalarAsync<int>(
                    "select count(*)::integer from unnest(private.expand_timetable_shape($1,$2,$3,$4,$5,$6,$7,$8,$9)) with ordinality expanded(id,name,start_time,end_time,is_lesson,ordinality) where expanded.ordinality=$10 and expanded.id=$11 and expanded.name=$12 and expanded.start_time=$13 and expanded.end_time=$14 and expanded.is_lesson=$15",
                    args));
            }
        }
        finally
        {
            foreach (Guid id in overrideIds)
                await fixture.SqlAsync("delete from public.anchor_date_overrides where id=$1", id);
        }
    }

    private static object[] ShapeArguments(Guid timetableId, ExpansionCase item) =>
    [
        timetableId, SupabaseFixture.OrgAId, item.Shape.DayStart, item.Shape.LessonCount,
        item.Shape.LessonMinutes, item.Shape.BreakAfterLesson is null ? DBNull.Value : item.Shape.BreakAfterLesson,
        item.Shape.BreakMinutes is null ? DBNull.Value : item.Shape.BreakMinutes,
        item.Shape.AdjustsForPrayer, item.Date
    ];

    private async Task<Dictionary<string, Guid>> AnchorIdsAsync()
    {
        string json = (await fixture.SqlScalarAsync<string>(
            "select jsonb_object_agg(key,id)::text from public.organization_anchors where org_id=$1",
            SupabaseFixture.OrgAId))!;
        JsonObject map = JsonNode.Parse(json)!.AsObject();
        return map.ToDictionary(item => item.Key, item => Guid.Parse(item.Value!.GetValue<string>()));
    }

    private async Task RunScheduledAsync()
    {
        using HttpResponseMessage response = await fixture.ServiceRestAsync(HttpMethod.Post,
            "rpc/run_generator_maintenance", new JsonObject { ["p_org_id"] = SupabaseFixture.OrgAId });
        response.EnsureSuccessStatusCode();
    }

    private Task<int> WrittenForDateAsync(DateOnly date) => fixture.SqlScalarAsync<int>(
        "select timetables_written from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2",
        SupabaseFixture.OrgAId, date);

    private Task<int> ExpandedAnchorAtAsync(Guid timetableId, DateOnly date, TimeOnly start) =>
        fixture.SqlScalarAsync<int>(
            "select count(*)::integer from unnest(private.expand_timetable_shape($1,$2,'12:30',4,30,null,null,true,$3)) expanded where not expanded.is_lesson and expanded.start_time=$4",
            timetableId, SupabaseFixture.OrgAId, date, start);

    private Task<int> ExpandedAnchorCountAsync(Guid timetableId, DateOnly date) =>
        fixture.SqlScalarAsync<int>(
            "select count(*)::integer from unnest(private.expand_timetable_shape($1,$2,'12:30',4,30,null,null,true,$3)) expanded where not expanded.is_lesson",
            timetableId, SupabaseFixture.OrgAId, date);

    private async Task<string> FingerprintAsync(Guid timetableId) => (await fixture.SqlScalarAsync<string>(
        "select md5(string_agg(concat_ws('|',id,name,start_time,end_time,sort_order,is_lesson), E'\\n' order by sort_order)) from public.periods where timetable_id=$1",
        timetableId))!;

    private Task<long> PeriodAuditCountAsync(Guid timetableId) => fixture.SqlScalarAsync<long>(
        "select count(*) from public.audit_log where entity_type='periods' and coalesce(after,before)->>'timetable_id'=$1",
        timetableId.ToString());

    private sealed record ExpansionCase(
        string Name, DateOnly Date, TimetableShape Shape, IReadOnlyList<ResolvedAnchor> Anchors);
}
