using System.Globalization;
using System.Text.Json.Nodes;

namespace AqiClock.SupabaseTests;

/// <summary>Release-gating allow/deny matrix for the generator tables.</summary>
[Collection("supabase")]
public sealed class GeneratorRlsMatrixTests(SupabaseFixture fixture)
{
    [SupabaseFact]
    public async Task GeneratorAdminWriteRpcsRejectAnonStaffAndServiceRole()
    {
        var saveBody = new JsonObject
        {
            ["p_timetable_id"] = Guid.NewGuid(), ["p_definition"] = new JsonObject(),
            ["p_blocks"] = new JsonArray(), ["p_anchor_ids"] = new JsonArray(), ["p_periods"] = new JsonArray(),
        };
        var bulkBody = new JsonObject { ["p_anchor_id"] = Guid.NewGuid(), ["p_rows"] = new JsonArray() };
        var previewBody = new JsonObject
        {
            ["p_timetable_id"] = Guid.NewGuid(), ["p_definition"] = new JsonObject(),
            ["p_blocks"] = new JsonArray(), ["p_anchor_ids"] = new JsonArray(),
        };
        foreach ((string rpc, JsonObject body) in (ValueTuple<string, JsonObject>[])
            [("admin_save_generated_timetable", saveBody), ("admin_bulk_upsert_anchor_date_overrides", bulkBody),
             ("admin_preview_generated_timetable", previewBody)])
        {
            using HttpResponseMessage anon = await fixture.RestAsync(TestPersona.Anon, HttpMethod.Post, $"rpc/{rpc}", body);
            using HttpResponseMessage staff = await fixture.RestAsync(TestPersona.Staff, HttpMethod.Post, $"rpc/{rpc}", body);
            using HttpResponseMessage service = await fixture.ServiceRestAsync(HttpMethod.Post, $"rpc/{rpc}", body);
            Assert.False(anon.IsSuccessStatusCode);
            Assert.False(staff.IsSuccessStatusCode);
            Assert.False(service.IsSuccessStatusCode);
        }
    }

    private static readonly string[] Tables =
    [
        "organization_anchors", "anchor_standing_times", "anchor_date_overrides",
        "timetable_generators", "timetable_generator_blocks", "timetable_generator_anchors",
        "generator_maintenance_runs",
    ];

    public static TheoryData<string, TestPersona, string, CellExpectation> Cases()
    {
        var data = new TheoryData<string, TestPersona, string, CellExpectation>();
        TestPersona[] personas =
            [TestPersona.Anon, TestPersona.Staff, TestPersona.Admin, TestPersona.Deactivated, TestPersona.CrossOrg];
        foreach (string table in Tables)
        foreach (TestPersona persona in personas)
        foreach (string operation in (string[])["select", "insert", "update", "delete"])
        {
            CellExpectation expected = operation == "select"
                ? persona switch
                {
                    TestPersona.Anon => CellExpectation.DeniedError,
                    TestPersona.Admin => CellExpectation.Visible,
                    TestPersona.Staff when table != "generator_maintenance_runs" => CellExpectation.Visible,
                    _ => CellExpectation.Hidden,
                }
                : persona == TestPersona.Admin && table != "generator_maintenance_runs"
                    ? CellExpectation.WriteAllowed
                    : CellExpectation.WriteDenied;
            data.Add(table, persona, operation, expected);
        }
        return data;
    }

    [SupabaseTheory]
    [MemberData(nameof(Cases))]
    public async Task Cell(string table, TestPersona persona, string operation, CellExpectation expected)
    {
        Probe probe = await PrepareAsync(table, operation);
        try
        {
            HttpMethod method = operation switch
            {
                "select" => HttpMethod.Get,
                "insert" => HttpMethod.Post,
                "update" => HttpMethod.Patch,
                "delete" => HttpMethod.Delete,
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
            };
            string path = operation switch
            {
                "select" => probe.SelectPath,
                "update" => probe.UpdatePath,
                "delete" => probe.DeletePath,
                _ => table,
            };
            JsonObject? body = operation switch
            {
                "insert" => probe.InsertBody,
                "update" => probe.UpdateBody,
                _ => null,
            };
            using HttpResponseMessage response = await fixture.RestAsync(persona, method, path, body);
            JsonArray? rows = await SupabaseFixture.RowsAsync(response);
            switch (expected)
            {
                case CellExpectation.Visible:
                case CellExpectation.WriteAllowed:
                    Assert.True(rows is { Count: 1 }, $"{persona} {operation} on {table} should affect one row (status {(int)response.StatusCode}).");
                    break;
                case CellExpectation.Hidden:
                case CellExpectation.WriteDenied:
                    Assert.True(rows is null or { Count: 0 }, $"{persona} {operation} on {table} must have no effect.");
                    break;
                case CellExpectation.DeniedError:
                    Assert.False(response.IsSuccessStatusCode, $"{persona} must have no grant on {table}.");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(expected), expected, null);
            }
        }
        finally
        {
            await probe.Cleanup();
        }
    }

    private async Task<Probe> PrepareAsync(string table, string operation)
    {
        Guid id = Guid.NewGuid();
        Guid timetableId = Guid.NewGuid();
        Guid anchorId = (await fixture.SqlScalarAsync<Guid?>(
            "select id from public.organization_anchors where org_id = $1 and key = 'zuhr'",
            SupabaseFixture.OrgAId))!.Value;
        string date = new DateOnly(2040, 1, 1).AddDays(Math.Abs(id.GetHashCode()) % 3000)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        async Task CleanupAsync()
        {
            await fixture.SqlAsync("delete from public.timetables where id = $1", timetableId);
            await fixture.SqlAsync("delete from public.anchor_standing_times where id = $1", id);
            await fixture.SqlAsync("delete from public.anchor_date_overrides where id = $1", id);
            await fixture.SqlAsync("delete from public.timetable_generator_blocks where id = $1", id);
            await fixture.SqlAsync("delete from public.generator_maintenance_runs where id = $1", id);
            await fixture.SqlAsync(
                "insert into public.organization_anchors (org_id,key,name,sort_order) values ($1,'zuhr','Zuhr',0),($1,'isha','Isha',3) on conflict (org_id,key) do update set name=excluded.name,sort_order=excluded.sort_order",
                SupabaseFixture.OrgAId);
        }

        if (table == "organization_anchors")
        {
            string key = operation == "insert" ? "isha" : "zuhr";
            int order = operation == "insert" ? 3 : 0;
            if (operation == "insert")
                await fixture.SqlAsync("delete from public.organization_anchors where org_id=$1 and key='isha'", SupabaseFixture.OrgAId);
            Guid existingId = operation == "insert" ? id : anchorId;
            return new(
                new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["key"] = key, ["name"] = key == "isha" ? "Isha" : "Zuhr", ["sort_order"] = order },
                $"{table}?id=eq.{existingId}&select=id", $"{table}?id=eq.{existingId}", $"{table}?id=eq.{existingId}",
                new JsonObject { ["name"] = "Zuhr" }, CleanupAsync);
        }

        bool needsGenerator = table is "timetable_generators" or "timetable_generator_blocks" or "timetable_generator_anchors";
        if (needsGenerator)
        {
            await fixture.SqlAsync("insert into public.timetables(id,org_id,name) values($1,$2,$3)", timetableId, SupabaseFixture.OrgAId, $"Generator RLS {id:N}");
            if (table != "timetable_generators" || operation != "insert")
                await fixture.SqlAsync("insert into public.timetable_generators(timetable_id,org_id,session_kind,day_start) values($1,$2,'am','09:10')", timetableId, SupabaseFixture.OrgAId);
        }

        switch (table)
        {
            case "generator_maintenance_runs":
                if (operation != "insert") await fixture.SqlAsync(
                    "insert into public.generator_maintenance_runs(id,org_id,started_at,duration_ms,regenerated_date,timetables_written) values($1,$2,now(),0,$3,0)",
                    id, SupabaseFixture.OrgAId, DateOnly.Parse(date, CultureInfo.InvariantCulture));
                return Standard(table, id,
                    new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["started_at"] = DateTimeOffset.UtcNow, ["duration_ms"] = 0, ["regenerated_date"] = date, ["timetables_written"] = 0 },
                    new JsonObject { ["duration_ms"] = 1 }, CleanupAsync);
            case "anchor_standing_times":
                if (operation != "insert") await fixture.SqlAsync(
                    "insert into public.anchor_standing_times(id,org_id,anchor_id,start_time,duration_minutes,effective_from) values($1,$2,$3,'13:37',10,$4)",
                    id, SupabaseFixture.OrgAId, anchorId, DateOnly.Parse(date, CultureInfo.InvariantCulture));
                return Standard(table, id,
                    new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["anchor_id"] = anchorId.ToString(), ["start_time"] = "13:37", ["duration_minutes"] = 10, ["effective_from"] = date },
                    new JsonObject { ["start_time"] = "13:37" }, CleanupAsync);
            case "anchor_date_overrides":
                if (operation != "insert") await fixture.SqlAsync(
                    "insert into public.anchor_date_overrides(id,org_id,anchor_id,date,start_time,duration_minutes) values($1,$2,$3,$4,'13:37',10)",
                    id, SupabaseFixture.OrgAId, anchorId, DateOnly.Parse(date, CultureInfo.InvariantCulture));
                return Standard(table, id,
                    new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["anchor_id"] = anchorId.ToString(), ["date"] = date, ["start_time"] = "13:37", ["duration_minutes"] = 10 },
                    new JsonObject { ["start_time"] = "13:37" }, CleanupAsync);
            case "timetable_generators":
                return new(
                    new JsonObject { ["timetable_id"] = timetableId.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["session_kind"] = "am", ["day_start"] = "09:10" },
                    $"{table}?timetable_id=eq.{timetableId}&select=timetable_id", $"{table}?timetable_id=eq.{timetableId}", $"{table}?timetable_id=eq.{timetableId}",
                    new JsonObject { ["day_start"] = "09:10" }, CleanupAsync);
            case "timetable_generator_blocks":
                if (operation != "insert") await fixture.SqlAsync(
                    "insert into public.timetable_generator_blocks(id,timetable_id,org_id,sort_order,block_kind,lesson_count,lesson_minutes) values($1,$2,$3,0,'lessons',4,30)",
                    id, timetableId, SupabaseFixture.OrgAId);
                return Standard(table, id,
                    new JsonObject { ["id"] = id.ToString(), ["timetable_id"] = timetableId.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["sort_order"] = 0, ["block_kind"] = "lessons", ["lesson_count"] = 4, ["lesson_minutes"] = 30 },
                    new JsonObject { ["lesson_count"] = 4 }, CleanupAsync);
            case "timetable_generator_anchors":
                if (operation != "insert") await fixture.SqlAsync(
                    "insert into public.timetable_generator_anchors(timetable_id,anchor_id,org_id) values($1,$2,$3)", timetableId, anchorId, SupabaseFixture.OrgAId);
                string filter = $"timetable_id=eq.{timetableId}&anchor_id=eq.{anchorId}";
                return new(
                    new JsonObject { ["timetable_id"] = timetableId.ToString(), ["anchor_id"] = anchorId.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString() },
                    $"{table}?{filter}&select=timetable_id", $"{table}?{filter}", $"{table}?{filter}",
                    new JsonObject { ["org_id"] = SupabaseFixture.OrgAId.ToString() }, CleanupAsync);
            default:
                throw new ArgumentOutOfRangeException(nameof(table), table, null);
        }
    }

    private static Probe Standard(string table, Guid id, JsonObject insert, JsonObject update, Func<Task> cleanup) =>
        new(insert, $"{table}?id=eq.{id}&select=id", $"{table}?id=eq.{id}", $"{table}?id=eq.{id}", update, cleanup);

    private sealed record Probe(JsonObject InsertBody, string SelectPath, string UpdatePath, string DeletePath, JsonObject UpdateBody, Func<Task> Cleanup);
}
