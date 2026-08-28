using System.Text.Json.Nodes;
using AqiClock.Domain.Scheduling;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class GeneratorMaintenanceTests(SupabaseFixture fixture)
{
    [SupabaseFact]
    public async Task SqlExpansionMatchesDomainFixtures()
    {
        Guid asr = await AnchorIdAsync("asr");
        Guid maghrib = await AnchorIdAsync("maghrib");
        Guid isha = await AnchorIdAsync("isha");
        Guid zuhr = await AnchorIdAsync("zuhr");
        ExpansionCase[] cases =
        [
            new("late winter Isha", new(2035, 1, 8), GeneratorSessionKind.Pm, new(18, 15),
                [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 5, 25)],
                [new(asr, "asr", "Asr", new(18, 40), 10), new(maghrib, "maghrib", "Maghrib", new(19, 30), 10), new(isha, "isha", "Isha", new(20, 30), 10)]),
            new("marginal two-pass host", new(2035, 1, 9), GeneratorSessionKind.Pm, new(18, 15),
                [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 4, 15)],
                [new(asr, "asr", "Asr", new(18, 20), 10), new(maghrib, "maghrib", "Maghrib", new(19, 35), 10)]),
            new("duplicate plain breaks", new(2035, 1, 10), GeneratorSessionKind.Am, new(9, 0),
                [new(Guid.NewGuid(), GeneratorBlockKind.Break, "Break", 1, 10), new(Guid.NewGuid(), GeneratorBlockKind.Break, "Break", 1, 10)], []),
            new("long anchor", new(2035, 1, 11), GeneratorSessionKind.Am, new(9, 0),
                [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 3, 30)],
                [new(zuhr, "zuhr", "Zuhr", new(9, 15), 70)]),
            new("authored naming pattern", new(2035, 1, 12), GeneratorSessionKind.Am, new(9, 0),
                [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 3, 20)], [], "Class {number}"),
        ];

        foreach (ExpansionCase expansionCase in cases)
            await AssertSqlDomainParityAsync(expansionCase);
    }

    [SupabaseFact]
    public async Task AnchorResolutionUsesNewestWeekdayThenDefaultAndHonoursCancellation()
    {
        Guid timetableId = Guid.NewGuid();
        Guid blockId = Guid.NewGuid();
        Guid zuhr = await AnchorIdAsync("zuhr");
        Guid defaultStanding = Guid.NewGuid();
        Guid olderMonday = Guid.NewGuid();
        Guid newerMonday = Guid.NewGuid();
        Guid cancellation = Guid.NewGuid();
        DateOnly monday = new(2035, 1, 8);
        DateOnly tuesday = monday.AddDays(1);
        DateOnly wednesday = monday.AddDays(2);
        try
        {
            await fixture.SqlAsync("insert into public.timetables(id,org_id,name,is_generated) values($1,$2,$3,true)", timetableId, SupabaseFixture.OrgAId, $"Resolution {timetableId:N}");
            await fixture.SqlAsync("insert into public.timetable_generators(timetable_id,org_id,session_kind,day_start) values($1,$2,'am','09:00')", timetableId, SupabaseFixture.OrgAId);
            await fixture.SqlAsync("insert into public.timetable_generator_blocks(id,timetable_id,org_id,sort_order,block_kind,lesson_count,lesson_minutes,hosts_naseehah) values($1,$2,$3,0,'lessons',8,30,false)", blockId, timetableId, SupabaseFixture.OrgAId);
            await fixture.SqlAsync("insert into public.timetable_generator_anchors(timetable_id,anchor_id,org_id) values($1,$2,$3)", timetableId, zuhr, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                """
                insert into public.anchor_standing_times(id,org_id,anchor_id,weekday,start_time,duration_minutes,effective_from)
                values ($1,$5,$6,null,'12:45',10,'2030-01-01'),
                       ($2,$5,$6,0,'12:50',10,'2034-01-01'),
                       ($3,$5,$6,0,'12:55',10,'2035-01-01')
                """, defaultStanding, olderMonday, newerMonday, cancellation, SupabaseFixture.OrgAId, zuhr);
            await fixture.SqlAsync(
                "insert into public.anchor_date_overrides(id,org_id,anchor_id,date,is_cancelled) values($1,$2,$3,$4,true)",
                cancellation, SupabaseFixture.OrgAId, zuhr, wednesday);

            Assert.Equal(1, await ExpandedAnchorAtAsync(timetableId, monday, new TimeOnly(12, 55)));
            Assert.Equal(1, await ExpandedAnchorAtAsync(timetableId, tuesday, new TimeOnly(12, 45)));
            Assert.Equal(0, await fixture.SqlScalarAsync<int>(
                "select count(*)::integer from unnest(private.expand_generated_timetable($1,$2)) expanded where not expanded.is_lesson",
                timetableId, wednesday));
        }
        finally
        {
            await fixture.SqlAsync("update public.timetables set is_generated=false where id=$1", timetableId);
            await fixture.SqlAsync("delete from public.timetables where id=$1", timetableId);
            await fixture.SqlAsync("delete from public.anchor_date_overrides where id=$1", cancellation);
            await fixture.SqlAsync("delete from public.anchor_standing_times where id in ($1,$2,$3)", defaultStanding, olderMonday, newerMonday);
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
        Assert.Equal(positiveLast, actual);
    }

    [SupabaseFact]
    public async Task SortOrderGapIsRepairedInsteadOfReadingAsUnchanged()
    {
        Guid timetableId = Guid.NewGuid();
        Guid blockId = Guid.NewGuid();
        DateOnly targetDate = DateOnly.FromDateTime(await fixture.SqlScalarAsync<DateTime>(
            "select timezone('Europe/London', now())::date"));
        try
        {
            await fixture.SqlAsync(
                "insert into public.timetables(id,org_id,name,is_generated) values ($1,$2,$3,true)",
                timetableId, SupabaseFixture.OrgAId, $"Sort gap {timetableId:N}");
            await fixture.SqlAsync(
                "insert into public.timetable_generators(timetable_id,org_id,session_kind,day_start) values ($1,$2,'am','09:00')",
                timetableId, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                "insert into public.timetable_generator_blocks(id,timetable_id,org_id,sort_order,block_kind,lesson_count,lesson_minutes,hosts_naseehah) values ($1,$2,$3,0,'lessons',3,20,false)",
                blockId, timetableId, SupabaseFixture.OrgAId);
            await RunScheduledAsync();
            await fixture.SqlAsync(
                "update public.periods set sort_order=sort_order+10 from (select set_config('aqi.generator_write','on',true)) configured where timetable_id=$1",
                timetableId);

            await RunScheduledAsync();

            Assert.Equal("0,1,2", await fixture.SqlScalarAsync<string>(
                "select string_agg(sort_order::text,',' order by sort_order) from public.periods where timetable_id=$1",
                timetableId));
            Assert.True(await WrittenForDateAsync(targetDate) > 0);
        }
        finally
        {
            await fixture.SqlAsync("delete from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2", SupabaseFixture.OrgAId, targetDate);
            await fixture.SqlAsync("update public.timetables set is_generated=false where id=$1", timetableId);
            await fixture.SqlAsync("delete from public.timetables where id=$1", timetableId);
        }
    }

    [SupabaseFact]
    public async Task MissingFridayZuhrDurationDoesNotBlockPmAndIsRecorded()
    {
        Guid amTimetable = Guid.NewGuid();
        Guid pmTimetable = Guid.NewGuid();
        Guid amBlock = Guid.NewGuid();
        Guid pmBlock = Guid.NewGuid();
        Guid fridayStanding = Guid.NewGuid();
        Guid currentOverride = Guid.NewGuid();
        Guid pmOverride = Guid.NewGuid();
        Guid zuhr = await AnchorIdAsync("zuhr");
        Guid maghrib = await AnchorIdAsync("maghrib");
        DateOnly friday = new(2032, 1, 2);
        DateOnly targetDate = DateOnly.FromDateTime(await fixture.SqlScalarAsync<DateTime>(
            "select timezone('Europe/London', now())::date"));

        try
        {
            await fixture.SqlAsync(
                "insert into public.timetables(id,org_id,name,is_generated) values ($1,$3,$4,true),($2,$3,$5,true)",
                amTimetable, pmTimetable, SupabaseFixture.OrgAId,
                $"Failure AM {amTimetable:N}", $"Failure PM {pmTimetable:N}");
            await fixture.SqlAsync(
                "insert into public.timetable_generators(timetable_id,org_id,session_kind,day_start) values ($1,$3,'am','09:10'),($2,$3,'pm','18:15')",
                amTimetable, pmTimetable, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                "insert into public.timetable_generator_blocks(id,timetable_id,org_id,sort_order,block_kind,lesson_count,lesson_minutes,hosts_naseehah) values ($1,$3,$5,0,'lessons',8,30,false),($2,$4,$5,0,'lessons',5,25,false)",
                amBlock, pmBlock, amTimetable, pmTimetable, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                "insert into public.timetable_generator_anchors(timetable_id,anchor_id,org_id) values ($1,$3,$5),($2,$4,$5)",
                amTimetable, pmTimetable, zuhr, maghrib, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                "insert into public.anchor_standing_times(id,org_id,anchor_id,weekday,start_time,duration_minutes,effective_from) values ($1,$2,$3,4,'12:58',null,'2030-01-01')",
                fridayStanding, SupabaseFixture.OrgAId, zuhr);

            Exception fridayFailure = await Assert.ThrowsAnyAsync<Exception>(() => fixture.SqlScalarAsync<int>(
                "select cardinality(private.expand_generated_timetable($1,$2))", amTimetable, friday));
            Assert.Contains("Zuhr", fridayFailure.Message, StringComparison.OrdinalIgnoreCase);

            await fixture.SqlAsync(
                "insert into public.anchor_date_overrides(id,org_id,anchor_id,date,start_time,duration_minutes) values ($1,$4,$5,$6,'12:58',null),($2,$4,$3,$6,'19:35',10)",
                currentOverride, pmOverride, maghrib, SupabaseFixture.OrgAId, zuhr, targetDate);
            await RunScheduledAsync();

            Assert.Equal(0, await fixture.SqlScalarAsync<int>(
                "select count(*)::integer from public.periods where timetable_id=$1", amTimetable));
            Assert.True(await fixture.SqlScalarAsync<int>(
                "select count(*)::integer from public.periods where timetable_id=$1", pmTimetable) > 0);
            Assert.Equal(1, await WrittenForDateAsync(targetDate));
            string error = (await fixture.SqlScalarAsync<string>(
                "select error from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2",
                SupabaseFixture.OrgAId, targetDate))!;
            Assert.Contains(amTimetable.ToString(), error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Zuhr", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await fixture.SqlAsync("delete from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2", SupabaseFixture.OrgAId, targetDate);
            await fixture.SqlAsync("update public.timetables set is_generated=false where id in ($1,$2)", amTimetable, pmTimetable);
            await fixture.SqlAsync("delete from public.timetables where id in ($1,$2)", amTimetable, pmTimetable);
            await fixture.SqlAsync("delete from public.anchor_date_overrides where id in ($1,$2)", currentOverride, pmOverride);
            await fixture.SqlAsync("delete from public.anchor_standing_times where id=$1", fridayStanding);
        }
    }

    [SupabaseFact]
    public async Task ConsecutiveDatesWriteOnlyTheTimetableWhoseExpansionChanged()
    {
        Guid amTimetable = Guid.NewGuid();
        Guid pmTimetable = Guid.NewGuid();
        Guid amBlock1 = Guid.NewGuid();
        Guid amBreak = Guid.NewGuid();
        Guid amBlock2 = Guid.NewGuid();
        Guid pmBlock = Guid.NewGuid();
        Guid zuhrStanding = Guid.NewGuid();
        Guid asrStanding = Guid.NewGuid();
        Guid firstMaghrib = Guid.NewGuid();
        Guid secondMaghrib = Guid.NewGuid();
        Guid zuhr = await AnchorIdAsync("zuhr");
        Guid asr = await AnchorIdAsync("asr");
        Guid maghrib = await AnchorIdAsync("maghrib");
        DateOnly firstDate = DateOnly.FromDateTime(await fixture.SqlScalarAsync<DateTime>(
            "select timezone('Pacific/Honolulu', now())::date"));
        DateOnly secondDate = firstDate.AddDays(1);

        try
        {
            await fixture.SqlAsync(
                "insert into public.timetables(id,org_id,name,is_generated) values ($1,$3,$4,true),($2,$3,$5,true)",
                amTimetable, pmTimetable, SupabaseFixture.OrgAId,
                $"Maintenance AM {amTimetable:N}", $"Maintenance PM {pmTimetable:N}");
            await fixture.SqlAsync(
                "insert into public.timetable_generators(timetable_id,org_id,session_kind,day_start) values ($1,$3,'am','09:10'),($2,$3,'pm','18:15')",
                amTimetable, pmTimetable, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                """
                insert into public.timetable_generator_blocks
                    (id,timetable_id,org_id,sort_order,block_kind,name,lesson_count,lesson_minutes,break_minutes,hosts_naseehah)
                values
                    ($1,$5,$7,0,'lessons',null,4,30,null,false),
                    ($2,$5,$7,1,'break','Break',null,null,25,true),
                    ($3,$5,$7,2,'lessons',null,4,30,null,false),
                    ($4,$6,$7,0,'lessons',null,5,25,null,false)
                """, amBlock1, amBreak, amBlock2, pmBlock, amTimetable, pmTimetable, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                "insert into public.timetable_generator_anchors(timetable_id,anchor_id,org_id) values ($1,$3,$6),($2,$4,$6),($2,$5,$6)",
                amTimetable, pmTimetable, zuhr, asr, maghrib, SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                "insert into public.anchor_standing_times(id,org_id,anchor_id,start_time,duration_minutes,effective_from) values ($1,$3,$4,'13:37',10,$6),($2,$3,$5,'18:40',10,$6)",
                zuhrStanding, asrStanding, SupabaseFixture.OrgAId, zuhr, asr, firstDate.AddDays(-30));
            await fixture.SqlAsync(
                "insert into public.anchor_date_overrides(id,org_id,anchor_id,date,start_time,duration_minutes) values ($1,$3,$4,$5,'20:12',10),($2,$3,$4,$6,'20:10',10)",
                firstMaghrib, secondMaghrib, SupabaseFixture.OrgAId, maghrib, firstDate, secondDate);
            await fixture.SqlAsync(
                "update public.organizations set timezone='Pacific/Honolulu' where id=$1",
                SupabaseFixture.OrgAId);

            await RunScheduledAsync();
            Assert.Equal(2, await WrittenForDateAsync(firstDate));
            await AssertExpansionAsync(amTimetable, AlQalamExpansionRules.Expand(
                amTimetable, GeneratorSessionKind.Am, new TimeOnly(9, 10),
                [
                    new GeneratorBlock(amBlock1, GeneratorBlockKind.Lessons, string.Empty, 4, 30),
                    new GeneratorBlock(amBreak, GeneratorBlockKind.Break, "Break", 1, 25, true),
                    new GeneratorBlock(amBlock2, GeneratorBlockKind.Lessons, string.Empty, 4, 30),
                ],
                [new ResolvedAnchor(zuhr, "zuhr", "Zuhr", new TimeOnly(13, 37), 10)]));
            await AssertExpansionAsync(pmTimetable, AlQalamExpansionRules.Expand(
                pmTimetable, GeneratorSessionKind.Pm, new TimeOnly(18, 15),
                [new GeneratorBlock(pmBlock, GeneratorBlockKind.Lessons, string.Empty, 5, 25)],
                [
                    new ResolvedAnchor(asr, "asr", "Asr", new TimeOnly(18, 40), 10),
                    new ResolvedAnchor(maghrib, "maghrib", "Maghrib", new TimeOnly(20, 12), 10),
                ]));
            string amBefore = await PeriodFingerprintAsync(amTimetable);
            string pmBefore = await PeriodFingerprintAsync(pmTimetable);
            long amAuditBefore = await PeriodAuditCountAsync(amTimetable);
            long pmAuditBefore = await PeriodAuditCountAsync(pmTimetable);

            await fixture.SqlAsync(
                "update public.organizations set timezone='Pacific/Kiritimati' where id=$1",
                SupabaseFixture.OrgAId);
            await RunScheduledAsync();

            Assert.Equal(1, await WrittenForDateAsync(secondDate));
            Assert.Equal(amBefore, await PeriodFingerprintAsync(amTimetable));
            Assert.NotEqual(pmBefore, await PeriodFingerprintAsync(pmTimetable));
            Assert.Equal(amAuditBefore, await PeriodAuditCountAsync(amTimetable));
            Assert.True(await PeriodAuditCountAsync(pmTimetable) > pmAuditBefore);

            long pmAuditAfterSecondDate = await PeriodAuditCountAsync(pmTimetable);
            await RunScheduledAsync();
            Assert.Equal(1, await fixture.SqlScalarAsync<long>(
                "select count(*) from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2",
                SupabaseFixture.OrgAId, secondDate));
            Assert.Equal(amAuditBefore, await PeriodAuditCountAsync(amTimetable));
            Assert.Equal(pmAuditAfterSecondDate, await PeriodAuditCountAsync(pmTimetable));
        }
        finally
        {
            await fixture.SqlAsync("delete from public.generator_maintenance_runs where org_id=$1 and regenerated_date in ($2,$3)",
                SupabaseFixture.OrgAId, firstDate, secondDate);
            await fixture.SqlAsync("update public.timetables set is_generated=false where id in ($1,$2)", amTimetable, pmTimetable);
            await fixture.SqlAsync("delete from public.timetables where id in ($1,$2)", amTimetable, pmTimetable);
            await fixture.SqlAsync("delete from public.anchor_date_overrides where id in ($1,$2)", firstMaghrib, secondMaghrib);
            await fixture.SqlAsync("delete from public.anchor_standing_times where id in ($1,$2)", zuhrStanding, asrStanding);
            await fixture.SqlAsync("update public.organizations set timezone='Europe/London' where id=$1", SupabaseFixture.OrgAId);
        }
    }

    [SupabaseFact]
    public async Task EntryPointsRejectTheOtherCallersKey()
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

    private async Task RunScheduledAsync()
    {
        using HttpResponseMessage response = await fixture.ServiceRestAsync(
            HttpMethod.Post, "rpc/run_generator_maintenance",
            new JsonObject { ["p_org_id"] = SupabaseFixture.OrgAId.ToString() });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> AnchorIdAsync(string key) =>
        (await fixture.SqlScalarAsync<Guid?>(
            "select id from public.organization_anchors where org_id=$1 and key=$2",
            SupabaseFixture.OrgAId, key))!.Value;

    private async Task<int> WrittenForDateAsync(DateOnly date) =>
        await fixture.SqlScalarAsync<int>(
            "select timetables_written from public.generator_maintenance_runs where org_id=$1 and regenerated_date=$2",
            SupabaseFixture.OrgAId, date);

    private Task<int> ExpandedAnchorAtAsync(Guid timetableId, DateOnly date, TimeOnly start) =>
        fixture.SqlScalarAsync<int>(
            "select count(*)::integer from unnest(private.expand_generated_timetable($1,$2)) expanded where not expanded.is_lesson and expanded.start_time=$3",
            timetableId, date, start);

    private async Task<string> PeriodFingerprintAsync(Guid timetableId) =>
        (await fixture.SqlScalarAsync<string>(
            """
            select md5(string_agg(concat_ws('|',id,name,start_time,end_time,sort_order,is_lesson), E'\n' order by sort_order))
            from public.periods where timetable_id=$1
            """, timetableId))!;

    private async Task<long> PeriodAuditCountAsync(Guid timetableId) =>
        await fixture.SqlScalarAsync<long>(
            """
            select count(*) from public.audit_log
            where entity_type='periods'
              and coalesce(after,before)->>'timetable_id'=$1
            """, timetableId.ToString());

    private async Task AssertExpansionAsync(Guid timetableId, GeneratorResult expected)
    {
        Assert.Equal(expected.Periods.Count, await fixture.SqlScalarAsync<int>(
            "select count(*)::integer from public.periods where timetable_id=$1", timetableId));
        for (int index = 0; index < expected.Periods.Count; index++)
        {
            GeneratedPeriod period = expected.Periods[index];
            Assert.Equal(1, await fixture.SqlScalarAsync<int>(
                """
                select count(*)::integer from public.periods
                where timetable_id=$1 and id=$2 and name=$3 and start_time=$4
                  and end_time=$5 and sort_order=$6 and is_lesson=$7
                """,
                timetableId, period.Id, period.Name, period.Start, period.End, index, period.IsLesson));
        }
    }

    private async Task AssertSqlDomainParityAsync(ExpansionCase expansionCase)
    {
        Guid timetableId = Guid.NewGuid();
        var overrideIds = new List<Guid>();
        try
        {
            await fixture.SqlAsync(
                "insert into public.timetables(id,org_id,name,is_generated) values ($1,$2,$3,true)",
                timetableId, SupabaseFixture.OrgAId, $"Parity {expansionCase.Name} {timetableId:N}");
            await fixture.SqlAsync(
                "insert into public.timetable_generators(timetable_id,org_id,session_kind,day_start,naming_pattern) values ($1,$2,$3,$4,$5)",
                timetableId, SupabaseFixture.OrgAId,
                expansionCase.Kind == GeneratorSessionKind.Am ? "am" : "pm",
                expansionCase.Start, expansionCase.NamingPattern);
            for (int index = 0; index < expansionCase.Blocks.Count; index++)
            {
                GeneratorBlock block = expansionCase.Blocks[index];
                await fixture.SqlAsync(
                    """
                    insert into public.timetable_generator_blocks
                        (id,timetable_id,org_id,sort_order,block_kind,name,lesson_count,lesson_minutes,break_minutes,hosts_naseehah)
                    values ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
                    """,
                    block.Id, timetableId, SupabaseFixture.OrgAId, index,
                    block.Kind == GeneratorBlockKind.Lessons ? "lessons" : "break", block.Name,
                    block.Kind == GeneratorBlockKind.Lessons ? block.Count : DBNull.Value,
                    block.Kind == GeneratorBlockKind.Lessons ? block.Minutes : DBNull.Value,
                    block.Kind == GeneratorBlockKind.Break ? block.Minutes : DBNull.Value,
                    block.HostsNaseehah);
            }
            foreach (ResolvedAnchor anchor in expansionCase.Anchors)
            {
                Guid overrideId = Guid.NewGuid();
                overrideIds.Add(overrideId);
                await fixture.SqlAsync(
                    "insert into public.timetable_generator_anchors(timetable_id,anchor_id,org_id) values ($1,$2,$3)",
                    timetableId, anchor.Id, SupabaseFixture.OrgAId);
                await fixture.SqlAsync(
                    "insert into public.anchor_date_overrides(id,org_id,anchor_id,date,start_time,duration_minutes) values ($1,$2,$3,$4,$5,$6)",
                    overrideId, SupabaseFixture.OrgAId, anchor.Id, expansionCase.Date, anchor.Start,
                    anchor.DurationMinutes is null ? DBNull.Value : anchor.DurationMinutes.Value);
            }

            GeneratorResult expected = AlQalamExpansionRules.Expand(
                timetableId, expansionCase.Kind, expansionCase.Start,
                expansionCase.Blocks, expansionCase.Anchors, namingPattern: expansionCase.NamingPattern);
            Assert.Equal(expected.Periods.Count, await fixture.SqlScalarAsync<int>(
                "select cardinality(private.expand_generated_timetable($1,$2))", timetableId, expansionCase.Date));
            for (int index = 0; index < expected.Periods.Count; index++)
            {
                GeneratedPeriod period = expected.Periods[index];
                Assert.Equal(1, await fixture.SqlScalarAsync<int>(
                    """
                    select count(*)::integer
                    from unnest(private.expand_generated_timetable($1,$2)) with ordinality
                        expanded(id,name,start_time,end_time,is_lesson,ordinality)
                    where expanded.ordinality=$3 and expanded.id=$4 and expanded.name=$5
                      and expanded.start_time=$6 and expanded.end_time=$7 and expanded.is_lesson=$8
                    """,
                    timetableId, expansionCase.Date, index + 1L, period.Id, period.Name,
                    period.Start, period.End, period.IsLesson));
            }
        }
        finally
        {
            await fixture.SqlAsync("update public.timetables set is_generated=false where id=$1", timetableId);
            await fixture.SqlAsync("delete from public.timetables where id=$1", timetableId);
            foreach (Guid overrideId in overrideIds)
                await fixture.SqlAsync("delete from public.anchor_date_overrides where id=$1", overrideId);
        }
    }

    private sealed record ExpansionCase(
        string Name,
        DateOnly Date,
        GeneratorSessionKind Kind,
        TimeOnly Start,
        IReadOnlyList<GeneratorBlock> Blocks,
        IReadOnlyList<ResolvedAnchor> Anchors,
        string NamingPattern = "Lesson {number}");
}
