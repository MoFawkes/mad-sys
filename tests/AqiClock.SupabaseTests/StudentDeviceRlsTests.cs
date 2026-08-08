using System.Net;
using System.Text.Json.Nodes;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Infrastructure.Supabase;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class StudentDeviceRlsTests(SupabaseFixture fixture)
{
    private static readonly Guid ClassId =
        Guid.Parse("00000000-0000-0000-0000-000000000501");
    private static readonly Guid EveryoneAnnouncementId =
        Guid.Parse("00000000-0000-0000-0000-000000000601");
    private static int _unique;

    public static TheoryData<string> ReadableScheduleTables() =>
        new()
        {
            "organizations",
            "timetables",
            "periods",
            "classes",
            "period_classes",
            "week_schedule",
            "date_overrides",
        };

    public static TheoryData<string> AllPublicTables() =>
        new()
        {
            "organizations",
            "profiles",
            "timetables",
            "periods",
            "classes",
            "period_classes",
            "week_schedule",
            "date_overrides",
            "announcements",
            "audit_log",
            "student_devices",
        };

    [SupabaseTheory]
    [MemberData(nameof(ReadableScheduleTables))]
    public async Task EnrolledDeviceCanReadItsOrganizationsPublishedSchedule(string table)
    {
        await EnsureProbeRowsAsync();

        using HttpResponseMessage response = await fixture.RestAsync(
            TestPersona.StudentDevice,
            HttpMethod.Get,
            ProbePath(table));
        JsonArray? rows = await SupabaseFixture.RowsAsync(response);

        Assert.True(
            rows is { Count: >= 1 },
            $"Enrolled student device should see {table} (status {(int)response.StatusCode}).");
    }

    [SupabaseFact]
    public async Task EnrolledDeviceReceivesOnlySafePublishedAnnouncementAudiences()
    {
        await EnsureProbeRowsAsync();

        using HttpResponseMessage response = await fixture.RestAsync(
            TestPersona.StudentDevice,
            HttpMethod.Get,
            "announcements?title=like.Student%20probe%25&select=id,audience_type,status");
        JsonArray rows = (await SupabaseFixture.RowsAsync(response))!;
        HashSet<Guid> actual = rows
            .Select(row => Guid.Parse(row!["id"]!.GetValue<string>()))
            .ToHashSet();

        var expected = new HashSet<Guid>
        {
            EveryoneAnnouncementId,
            Guid.Parse("00000000-0000-0000-0000-000000000602"),
            Guid.Parse("00000000-0000-0000-0000-000000000603"),
            Guid.Parse("00000000-0000-0000-0000-000000000604"),
        };
        Assert.True(expected.SetEquals(actual));
    }

    [SupabaseFact]
    public async Task EnrolledDeviceCannotEnumerateProfilesOrAuditLog()
    {
        foreach (string table in (string[])["profiles", "audit_log"])
        {
            using HttpResponseMessage response = await fixture.RestAsync(
                TestPersona.StudentDevice, HttpMethod.Get, $"{table}?select=*");
            Assert.Empty((await SupabaseFixture.RowsAsync(response))!);
        }

        Assert.Equal(0L, await fixture.SqlScalarAsync<long>(
            "select count(*) from public.profiles where id = $1",
            fixture.StudentDeviceUserId));
    }

    [SupabaseTheory]
    [MemberData(nameof(AllPublicTables))]
    public async Task UnenrolledAnonymousUserReadsNothing(string table)
    {
        await EnsureProbeRowsAsync();

        using HttpResponseMessage response = await fixture.RestAsync(
            TestPersona.UnenrolledStudent, HttpMethod.Get, $"{table}?select=*");
        JsonArray? rows = await SupabaseFixture.RowsAsync(response);

        Assert.True(
            rows is { Count: 0 },
            $"Unenrolled anonymous user must see no {table} rows (status {(int)response.StatusCode}).");
    }

    [SupabaseTheory]
    [MemberData(nameof(AllPublicTables))]
    public async Task EnrolledDeviceCannotInsertUpdateOrDeleteAnyTable(string table)
    {
        await EnsureProbeRowsAsync();
        if (table == "week_schedule")
        {
            await fixture.SqlAsync(
                "delete from public.week_schedule where org_id = $1 and weekday = 6",
                SupabaseFixture.OrgAId);
        }

        try
        {
            using HttpResponseMessage insert = await fixture.RestAsync(
                TestPersona.StudentDevice, HttpMethod.Post, table, InsertBody(table));
            AssertWriteDenied(table, "insert", insert, await SupabaseFixture.RowsAsync(insert));

            using HttpResponseMessage update = await fixture.RestAsync(
                TestPersona.StudentDevice, HttpMethod.Patch, UpdatePath(table), UpdateBody(table));
            AssertWriteDenied(table, "update", update, await SupabaseFixture.RowsAsync(update));

            using HttpResponseMessage delete = await fixture.RestAsync(
                TestPersona.StudentDevice, HttpMethod.Delete, DeletePath(table));
            AssertWriteDenied(table, "delete", delete, await SupabaseFixture.RowsAsync(delete));
        }
        finally
        {
            if (table == "week_schedule")
            {
                await fixture.SqlAsync(
                    """
                    insert into public.week_schedule (id, org_id, weekday, timetable_id)
                    values ('00000000-0000-0000-0000-000000000206', $1, 6, null)
                    on conflict on constraint week_schedule_org_weekday_audience_key do nothing
                    """,
                    SupabaseFixture.OrgAId);
            }
        }
    }

    [SupabaseFact]
    public async Task WrongJoinCodeRaisesInsufficientPrivilege()
    {
        using HttpResponseMessage response = await fixture.RestAsync(
            TestPersona.UnenrolledStudent,
            HttpMethod.Post,
            "rpc/enroll_student_device",
            new JsonObject { ["join_code"] = "WRONGCODE99" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("42501", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [SupabaseFact]
    public async Task PermanentUserCannotCallStudentEnrollmentRpc()
    {
        using HttpResponseMessage response = await fixture.RestAsync(
            TestPersona.Staff,
            HttpMethod.Post,
            "rpc/enroll_student_device",
            new JsonObject { ["join_code"] = fixture.StudentJoinCode });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("42501", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [SupabaseFact]
    public async Task DesktopGatewayCanSignInEnrollAndResolveStudentOrganization()
    {
        using var gateway = new SupabaseGateway(Options.Create(new SupabaseOptions
        {
            Url = SupabaseEnvironment.Url!,
            AnonKey = SupabaseEnvironment.AnonKey,
        }), NullLogger<SupabaseGateway>.Instance);
        AuthenticatedSession session = await gateway.SignInAnonymouslyAsync();
        try
        {
            Guid enrolledOrganization = await gateway.EnrollStudentDeviceAsync(fixture.StudentJoinCode);
            Assert.Equal(SupabaseFixture.OrgAId, enrolledOrganization);
            Assert.Equal(SupabaseFixture.OrgAId, await gateway.GetCurrentOrganizationIdAsync());
            Assert.NotEmpty((await gateway.PullAsync(CacheTable.Organizations)).Rows);
        }
        finally
        {
            await fixture.SqlAsync("delete from auth.users where id = $1", session.UserId);
        }
    }

    private async Task EnsureProbeRowsAsync()
    {
        await fixture.SqlAsync(
            """
            insert into public.classes (id, org_id, name, sort_order)
            values ($1, $2, 'Student probe class', 999)
            on conflict (id) do update set name = excluded.name
            """,
            ClassId, SupabaseFixture.OrgAId);
        await fixture.SqlAsync(
            """
            insert into public.period_classes (period_id, class_id)
            values ($1, $2)
            on conflict do nothing
            """,
            SupabaseFixture.SeedPeriodRegistrationId, ClassId);

        string sql =
            """
            insert into public.announcements
              (id, org_id, title, body, created_by, audience_type, audience_class_id,
               status, publish_at, deleted_at)
            values
              ('00000000-0000-0000-0000-000000000601', $1, 'Student probe everyone', 'Body', $2, 'everyone', null, 'published', now() - interval '1 minute', null),
              ('00000000-0000-0000-0000-000000000602', $1, 'Student probe am', 'Body', $2, 'am', null, 'published', now() - interval '1 minute', null),
              ('00000000-0000-0000-0000-000000000603', $1, 'Student probe pm', 'Body', $2, 'pm', null, 'published', now() - interval '1 minute', null),
              ('00000000-0000-0000-0000-000000000604', $1, 'Student probe class', 'Body', $2, 'specific_class', $3, 'published', now() - interval '1 minute', null),
              ('00000000-0000-0000-0000-000000000605', $1, 'Student probe teachers', 'Body', $2, 'teachers', null, 'published', now() - interval '1 minute', null),
              ('00000000-0000-0000-0000-000000000606', $1, 'Student probe graduates', 'Body', $2, 'graduates', null, 'published', now() - interval '1 minute', null),
              ('00000000-0000-0000-0000-000000000607', $1, 'Student probe draft', 'Body', $2, 'everyone', null, 'draft', now() - interval '1 minute', null),
              ('00000000-0000-0000-0000-000000000608', $1, 'Student probe future', 'Body', $2, 'everyone', null, 'scheduled', now() + interval '1 day', null),
              ('00000000-0000-0000-0000-000000000609', $1, 'Student probe deleted', 'Body', $2, 'everyone', null, 'published', now() - interval '1 minute', now())
            on conflict (id) do update set
              audience_type = excluded.audience_type,
              audience_class_id = excluded.audience_class_id,
              status = excluded.status,
              publish_at = excluded.publish_at,
              deleted_at = excluded.deleted_at
            """;
        await fixture.SqlAsync(sql, SupabaseFixture.OrgAId, fixture.AdminUserId, ClassId);
    }

    private string ProbePath(string table) => table switch
    {
        "organizations" => $"organizations?id=eq.{SupabaseFixture.OrgAId}&select=id",
        "timetables" => $"timetables?id=eq.{SupabaseFixture.SeedTimetableId}&select=id",
        "periods" => $"periods?id=eq.{SupabaseFixture.SeedPeriodRegistrationId}&select=id",
        "classes" => $"classes?id=eq.{ClassId}&select=id",
        "period_classes" => $"period_classes?period_id=eq.{SupabaseFixture.SeedPeriodRegistrationId}&class_id=eq.{ClassId}&select=period_id",
        "week_schedule" => $"week_schedule?id=eq.{SupabaseFixture.SeedWeekdayMondayId}&select=id",
        "date_overrides" => $"date_overrides?id=eq.{fixture.ProbeDateOverrideId}&select=id",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    private JsonObject InsertBody(string table)
    {
        int unique = Interlocked.Increment(ref _unique);
        Guid id = Guid.NewGuid();
        return table switch
        {
            "organizations" => new JsonObject { ["id"] = id.ToString(), ["name"] = $"Student denied {unique}" },
            "profiles" => new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["display_name"] = "Denied" },
            "timetables" => new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["name"] = $"Student denied {unique}" },
            "periods" => new JsonObject { ["id"] = id.ToString(), ["timetable_id"] = SupabaseFixture.SeedTimetableId.ToString(), ["name"] = $"Student denied {unique}", ["start_time"] = "16:00", ["end_time"] = "16:30", ["sort_order"] = 9000 + unique },
            "classes" => new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["name"] = $"Student denied {unique}", ["sort_order"] = 9000 + unique },
            "period_classes" => new JsonObject { ["period_id"] = "00000000-0000-0000-0000-000000000302", ["class_id"] = ClassId.ToString() },
            "week_schedule" => new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["weekday"] = 6 },
            "date_overrides" => new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["date"] = "2045-01-01" },
            "announcements" => new JsonObject { ["id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["title"] = "Denied", ["body"] = "Denied", ["created_by"] = fixture.AdminUserId.ToString() },
            "audit_log" => new JsonObject { ["org_id"] = SupabaseFixture.OrgAId.ToString(), ["action"] = "insert", ["entity_type"] = "forged", ["entity_id"] = id.ToString() },
            "student_devices" => new JsonObject { ["user_id"] = id.ToString(), ["org_id"] = SupabaseFixture.OrgAId.ToString() },
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
        };
    }

    private string UpdatePath(string table) => table switch
    {
        "organizations" => $"organizations?id=eq.{SupabaseFixture.OrgAId}",
        "profiles" => $"profiles?id=eq.{fixture.AdminUserId}",
        "timetables" => $"timetables?id=eq.{SupabaseFixture.SeedTimetableId}",
        "periods" => $"periods?id=eq.{SupabaseFixture.SeedPeriodRegistrationId}",
        "classes" => $"classes?id=eq.{ClassId}",
        "period_classes" => $"period_classes?period_id=eq.{SupabaseFixture.SeedPeriodRegistrationId}&class_id=eq.{ClassId}",
        "week_schedule" => $"week_schedule?id=eq.{SupabaseFixture.SeedWeekdayMondayId}",
        "date_overrides" => $"date_overrides?id=eq.{fixture.ProbeDateOverrideId}",
        "announcements" => $"announcements?id=eq.{EveryoneAnnouncementId}",
        "audit_log" => "audit_log?id=gt.0",
        "student_devices" => $"student_devices?user_id=eq.{fixture.StudentDeviceUserId}",
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    private static JsonObject UpdateBody(string table) => table switch
    {
        "organizations" => new JsonObject { ["name"] = "AQI" },
        "profiles" => new JsonObject { ["display_name"] = "Denied" },
        "timetables" => new JsonObject { ["name"] = "Normal Day" },
        "periods" => new JsonObject { ["name"] = "Registration" },
        "classes" => new JsonObject { ["name"] = "Student probe class" },
        "period_classes" => new JsonObject { ["class_id"] = ClassId.ToString() },
        "week_schedule" => new JsonObject { ["timetable_id"] = null },
        "date_overrides" => new JsonObject { ["note"] = "Probe override" },
        "announcements" => new JsonObject { ["title"] = "Student probe everyone" },
        "audit_log" => new JsonObject { ["action"] = "update" },
        "student_devices" => new JsonObject { ["last_seen_at"] = DateTimeOffset.UtcNow.ToString("O") },
        _ => throw new ArgumentOutOfRangeException(nameof(table), table, null),
    };

    private string DeletePath(string table) => UpdatePath(table);

    private static void AssertWriteDenied(
        string table,
        string operation,
        HttpResponseMessage response,
        JsonArray? rows)
    {
        Assert.True(
            rows is null or { Count: 0 },
            $"Student device {operation} on {table} must have no effect (status {(int)response.StatusCode}).");
    }
}
