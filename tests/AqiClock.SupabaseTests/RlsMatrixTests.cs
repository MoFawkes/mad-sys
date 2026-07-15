using System.Globalization;
using System.Text.Json.Nodes;

namespace AqiClock.SupabaseTests;

public enum CellExpectation
{
    /// <summary>SELECT succeeds and the probe row is returned.</summary>
    Visible,

    /// <summary>SELECT succeeds but returns zero rows (RLS filters silently).</summary>
    Hidden,

    /// <summary>The request itself is rejected (no grant / anon).</summary>
    DeniedError,

    /// <summary>The write succeeds and affects at least one row.</summary>
    WriteAllowed,

    /// <summary>The write either errors or affects zero rows — and must have no effect.</summary>
    WriteDenied,
}

/// <summary>
/// The release-blocking RLS matrix: 8 tables × 5 personas × 4 operations, asserting both
/// expected-allow (with effect) and expected-deny per SECURITY.md §3. Deny for UPDATE/DELETE
/// accepts either an error or a zero-row result — PostgREST reports RLS-filtered writes as
/// successful no-ops, which is still "no effect".
/// </summary>
[Collection("supabase")]
public sealed class RlsMatrixTests(SupabaseFixture fixture)
{
    private static int _uniqueCounter;

    public static TheoryData<string, TestPersona, string, CellExpectation> Cases()
    {
        var data = new TheoryData<string, TestPersona, string, CellExpectation>();
        TestPersona[] personas =
            [TestPersona.Anon, TestPersona.Staff, TestPersona.Admin, TestPersona.Deactivated, TestPersona.CrossOrg];
        string[] adminWritable = ["timetables", "periods", "week_schedule", "date_overrides", "announcements"];

        foreach (TestPersona persona in personas)
        {
            foreach (string table in adminWritable)
            {
                data.Add(table, persona, "select", SelectExpectation(persona, adminVisibleOnly: false));
                foreach (string op in (string[])["insert", "update", "delete"])
                {
                    data.Add(table, persona, op,
                        persona == TestPersona.Admin ? CellExpectation.WriteAllowed : CellExpectation.WriteDenied);
                }
            }

            // organizations: readable by members, writable by nobody (dashboard only).
            data.Add("organizations", persona, "select", SelectExpectation(persona, adminVisibleOnly: false));
            foreach (string op in (string[])["insert", "update", "delete"])
            {
                data.Add("organizations", persona, op, CellExpectation.WriteDenied);
            }

            // profiles: no client INSERT/DELETE; UPDATE allowed for self (active) and admins.
            data.Add("profiles", persona, "select", SelectExpectation(persona, adminVisibleOnly: false));
            data.Add("profiles", persona, "insert", CellExpectation.WriteDenied);
            data.Add("profiles", persona, "update", persona switch
            {
                TestPersona.Admin or TestPersona.Staff or TestPersona.CrossOrg => CellExpectation.WriteAllowed,
                _ => CellExpectation.WriteDenied,
            });
            data.Add("profiles", persona, "delete", CellExpectation.WriteDenied);

            // audit_log: admin-only reads, no client writes at all.
            data.Add("audit_log", persona, "select", SelectExpectation(persona, adminVisibleOnly: true));
            foreach (string op in (string[])["insert", "update", "delete"])
            {
                data.Add("audit_log", persona, op, CellExpectation.WriteDenied);
            }
        }

        return data;
    }

    private static CellExpectation SelectExpectation(TestPersona persona, bool adminVisibleOnly) => persona switch
    {
        TestPersona.Anon => CellExpectation.DeniedError,
        TestPersona.Admin => CellExpectation.Visible,
        TestPersona.Staff when !adminVisibleOnly => CellExpectation.Visible,
        _ => CellExpectation.Hidden,
    };

    [SupabaseTheory]
    [MemberData(nameof(Cases))]
    public async Task Cell(string table, TestPersona persona, string op, CellExpectation expected)
    {
        switch (op)
        {
            case "select":
                await AssertSelectAsync(table, persona, expected);
                break;
            case "insert":
                await AssertInsertAsync(table, persona, expected);
                break;
            case "update":
                await AssertUpdateAsync(table, persona, expected);
                break;
            case "delete":
                await AssertDeleteAsync(table, persona, expected);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(op), op, null);
        }
    }

    [SupabaseTheory]
    [InlineData("timetables")]
    [InlineData("periods")]
    [InlineData("week_schedule")]
    [InlineData("date_overrides")]
    [InlineData("announcements")]
    public async Task AdminCannotInsertRowsIntoAnotherOrganization(string table)
    {
        Guid foreignTimetableId = Guid.NewGuid();
        await fixture.SqlAsync(
            "insert into public.timetables (id, org_id, name) values ($1, $2, $3)",
            foreignTimetableId, fixture.OrgBId, $"Foreign {fixture.RunId} {foreignTimetableId}");

        JsonObject body = table switch
        {
            "timetables" => new JsonObject
            {
                ["org_id"] = fixture.OrgBId.ToString(),
                ["name"] = $"Cross-org {Guid.NewGuid()}",
            },
            "periods" => new JsonObject
            {
                ["timetable_id"] = foreignTimetableId.ToString(),
                ["name"] = $"Cross-org {Guid.NewGuid()}",
                ["start_time"] = "15:00",
                ["end_time"] = "15:30",
                ["sort_order"] = 1,
            },
            "week_schedule" => new JsonObject
            {
                ["org_id"] = fixture.OrgBId.ToString(),
                ["weekday"] = 0,
            },
            "date_overrides" => new JsonObject
            {
                ["org_id"] = fixture.OrgBId.ToString(),
                ["date"] = "2040-01-01",
            },
            "announcements" => new JsonObject
            {
                ["org_id"] = fixture.OrgBId.ToString(),
                ["title"] = "Cross-org announcement",
                ["body"] = "Must be denied",
                ["created_by"] = fixture.AdminUserId.ToString(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };

        using HttpResponseMessage response = await fixture.RestAsync(
            TestPersona.Admin, HttpMethod.Post, table, body);

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("42501", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---- SELECT -------------------------------------------------------------

    private async Task AssertSelectAsync(string table, TestPersona persona, CellExpectation expected)
    {
        HttpResponseMessage response = await fixture.RestAsync(persona, HttpMethod.Get, ProbeSelectPath(table));
        JsonArray? rows = await SupabaseFixture.RowsAsync(response);

        switch (expected)
        {
            case CellExpectation.Visible:
                Assert.True(rows is { Count: >= 1 }, $"{persona} should see the {table} probe row (status {(int)response.StatusCode}).");
                break;
            case CellExpectation.Hidden:
                Assert.True(rows is { Count: 0 }, $"{persona} must get an empty (not error) result on {table} (status {(int)response.StatusCode}).");
                break;
            case CellExpectation.DeniedError:
                Assert.False(response.IsSuccessStatusCode, $"{persona} must be rejected outright on {table}.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expected), expected, null);
        }
    }

    private string ProbeSelectPath(string table) => table switch
    {
        "organizations" => $"organizations?id=eq.{SupabaseFixture.OrgAId}&select=id",
        "profiles" => $"profiles?id=eq.{fixture.StaffUserId}&select=id",
        "timetables" => $"timetables?id=eq.{SupabaseFixture.SeedTimetableId}&select=id",
        "periods" => $"periods?id=eq.{SupabaseFixture.SeedPeriodRegistrationId}&select=id",
        "week_schedule" => $"week_schedule?id=eq.{SupabaseFixture.SeedWeekdayMondayId}&select=id",
        "date_overrides" => $"date_overrides?id=eq.{fixture.ProbeDateOverrideId}&select=id",
        "announcements" => $"announcements?id=eq.{fixture.ProbeAnnouncementId}&select=id",
        "audit_log" => $"audit_log?entity_id=eq.{SupabaseFixture.SeedTimetableId}&select=id&limit=1",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    // ---- INSERT -------------------------------------------------------------

    private async Task AssertInsertAsync(string table, TestPersona persona, CellExpectation expected)
    {
        if (table == "week_schedule")
        {
            // Free weekday 6 so every persona hits RLS, never the unique constraint.
            await fixture.SqlAsync(
                "delete from public.week_schedule where org_id = $1 and weekday = 6", SupabaseFixture.OrgAId);
        }

        try
        {
            HttpResponseMessage response =
                await fixture.RestAsync(persona, HttpMethod.Post, table, InsertBody(table));
            JsonArray? rows = await SupabaseFixture.RowsAsync(response);

            if (expected == CellExpectation.WriteAllowed)
            {
                Assert.True(rows is { Count: 1 }, $"{persona} insert into {table} should succeed (status {(int)response.StatusCode}).");
            }
            else
            {
                Assert.True(rows is null or { Count: 0 }, $"{persona} insert into {table} must be denied.");
            }
        }
        finally
        {
            if (table == "week_schedule")
            {
                await fixture.SqlAsync(
                    "delete from public.week_schedule where org_id = $1 and weekday = 6", SupabaseFixture.OrgAId);
                await fixture.SqlAsync(
                    "insert into public.week_schedule (id, org_id, weekday, timetable_id) values ('00000000-0000-0000-0000-000000000206', $1, 6, null) on conflict (org_id, weekday) do nothing",
                    SupabaseFixture.OrgAId);
            }
        }
    }

    private JsonObject InsertBody(string table)
    {
        int unique = Interlocked.Increment(ref _uniqueCounter);
        string tag = $"{fixture.RunId}-{unique}";
        return table switch
        {
            "timetables" => new JsonObject
            {
                ["org_id"] = SupabaseFixture.OrgAId.ToString(),
                ["name"] = $"Matrix timetable {tag}",
            },
            "periods" => new JsonObject
            {
                ["timetable_id"] = SupabaseFixture.SeedTimetableId.ToString(),
                ["name"] = $"Matrix period {tag}",
                ["start_time"] = "13:00",
                ["end_time"] = "14:00",
                ["sort_order"] = 1000 + unique,
            },
            "week_schedule" => new JsonObject
            {
                ["org_id"] = SupabaseFixture.OrgAId.ToString(),
                ["weekday"] = 6,
            },
            "date_overrides" => new JsonObject
            {
                ["org_id"] = SupabaseFixture.OrgAId.ToString(),
                ["date"] = new DateOnly(2032, 1, 1).AddDays(unique).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["note"] = $"Matrix override {tag}",
            },
            "announcements" => new JsonObject
            {
                ["org_id"] = SupabaseFixture.OrgAId.ToString(),
                ["title"] = $"Matrix announcement {tag}",
                ["body"] = "Matrix body",
                ["created_by"] = fixture.AdminUserId.ToString(),
            },
            "organizations" => new JsonObject { ["name"] = $"Matrix org {tag}" },
            "profiles" => new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["org_id"] = SupabaseFixture.OrgAId.ToString(),
                ["display_name"] = $"Matrix profile {tag}",
            },
            "audit_log" => new JsonObject
            {
                ["org_id"] = SupabaseFixture.OrgAId.ToString(),
                ["action"] = "insert",
                ["entity_type"] = "forged",
                ["entity_id"] = Guid.NewGuid().ToString(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };
    }

    // ---- UPDATE -------------------------------------------------------------

    private async Task AssertUpdateAsync(string table, TestPersona persona, CellExpectation expected)
    {
        (string path, JsonObject body) = UpdateTarget(table, persona);
        HttpResponseMessage response = await fixture.RestAsync(persona, HttpMethod.Patch, path, body);
        JsonArray? rows = await SupabaseFixture.RowsAsync(response);

        if (expected == CellExpectation.WriteAllowed)
        {
            Assert.True(rows is { Count: >= 1 }, $"{persona} update on {table} should succeed (status {(int)response.StatusCode}).");
        }
        else
        {
            Assert.True(rows is null or { Count: 0 }, $"{persona} update on {table} must have no effect.");
        }
    }

    private (string Path, JsonObject Body) UpdateTarget(string table, TestPersona persona) => table switch
    {
        // Same-value updates: prove write access without disturbing shared state.
        "timetables" => ($"timetables?id=eq.{SupabaseFixture.SeedTimetableId}", new JsonObject { ["name"] = "Normal Day" }),
        "periods" => ($"periods?id=eq.{SupabaseFixture.SeedPeriodRegistrationId}", new JsonObject { ["name"] = "Registration" }),
        "week_schedule" => ($"week_schedule?id=eq.{SupabaseFixture.SeedWeekdayMondayId}", new JsonObject { ["timetable_id"] = null }),
        "date_overrides" => ($"date_overrides?id=eq.{fixture.ProbeDateOverrideId}", new JsonObject { ["note"] = "Probe override" }),
        "announcements" => ($"announcements?id=eq.{fixture.ProbeAnnouncementId}", new JsonObject { ["title"] = "Probe announcement" }),
        "organizations" => ($"organizations?id=eq.{SupabaseFixture.OrgAId}", new JsonObject { ["name"] = "AQI" }),
        "profiles" => (
            $"profiles?id=eq.{ProfileUpdateTarget(persona)}",
            new JsonObject { ["display_name"] = $"Updated by {persona}" }),
        "audit_log" => ("audit_log?id=gt.0", new JsonObject { ["action"] = "update" }),
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    private Guid ProfileUpdateTarget(TestPersona persona) => persona switch
    {
        TestPersona.Staff => fixture.StaffUserId,
        TestPersona.CrossOrg => fixture.CrossOrgUserId,
        TestPersona.Deactivated => fixture.DeactivatedUserId,
        _ => fixture.StaffUserId, // admin targets staff1; anon targets staff1 and is rejected
    };

    // ---- DELETE -------------------------------------------------------------

    private async Task AssertDeleteAsync(string table, TestPersona persona, CellExpectation expected)
    {
        string path;
        if (expected == CellExpectation.WriteAllowed)
        {
            path = await PrepareDisposableRowAsync(table);
        }
        else
        {
            path = DeleteDenyPath(table);
        }

        HttpResponseMessage response = await fixture.RestAsync(persona, HttpMethod.Delete, path);
        JsonArray? rows = await SupabaseFixture.RowsAsync(response);

        if (expected == CellExpectation.WriteAllowed)
        {
            Assert.True(rows is { Count: 1 }, $"{persona} delete on {table} should succeed (status {(int)response.StatusCode}).");
            if (table == "week_schedule")
            {
                await fixture.SqlAsync(
                    "insert into public.week_schedule (id, org_id, weekday, timetable_id) values ('00000000-0000-0000-0000-000000000206', $1, 6, null) on conflict (org_id, weekday) do nothing",
                    SupabaseFixture.OrgAId);
            }
        }
        else
        {
            Assert.True(rows is null or { Count: 0 }, $"{persona} delete on {table} must have no effect.");
        }
    }

    private string DeleteDenyPath(string table) => table switch
    {
        "organizations" => $"organizations?id=eq.{SupabaseFixture.OrgAId}",
        "profiles" => $"profiles?id=eq.{fixture.StaffUserId}",
        "timetables" => $"timetables?id=eq.{SupabaseFixture.SeedTimetableId}",
        "periods" => $"periods?id=eq.{SupabaseFixture.SeedPeriodRegistrationId}",
        "week_schedule" => $"week_schedule?id=eq.{SupabaseFixture.SeedWeekdayMondayId}",
        "date_overrides" => $"date_overrides?id=eq.{fixture.ProbeDateOverrideId}",
        "announcements" => $"announcements?id=eq.{fixture.ProbeAnnouncementId}",
        "audit_log" => "audit_log?id=gt.0",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    private async Task<string> PrepareDisposableRowAsync(string table)
    {
        int unique = Interlocked.Increment(ref _uniqueCounter);
        var id = Guid.NewGuid();
        switch (table)
        {
            case "timetables":
                await fixture.SqlAsync(
                    "insert into public.timetables (id, org_id, name) values ($1, $2, $3)",
                    id, SupabaseFixture.OrgAId, $"Disposable {fixture.RunId}-{unique}");
                return $"timetables?id=eq.{id}";
            case "periods":
                await fixture.SqlAsync(
                    "insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order) values ($1, $2, $3, '15:00', '15:30', $4)",
                    id, SupabaseFixture.SeedTimetableId, $"Disposable {fixture.RunId}-{unique}", 5000 + unique);
                return $"periods?id=eq.{id}";
            case "week_schedule":
                return "week_schedule?org_id=eq." + SupabaseFixture.OrgAId + "&weekday=eq.6";
            case "date_overrides":
                await fixture.SqlAsync(
                    "insert into public.date_overrides (id, org_id, date, note) values ($1, $2, $3, 'disposable')",
                    id, SupabaseFixture.OrgAId, new DateOnly(2033, 1, 1).AddDays(unique));
                return $"date_overrides?id=eq.{id}";
            case "announcements":
                await fixture.SqlAsync(
                    "insert into public.announcements (id, org_id, title, body, created_by) values ($1, $2, 'Disposable', 'Disposable', $3)",
                    id, SupabaseFixture.OrgAId, fixture.AdminUserId);
                return $"announcements?id=eq.{id}";
            default:
                throw new ArgumentOutOfRangeException(nameof(table), table, null);
        }
    }
}
