using System.Net;
using System.Text.Json.Nodes;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class JoinCodeAdminTests(SupabaseFixture fixture)
{
    public static TheoryData<TestPersona> NonAdminPersonas() =>
        new()
        {
            TestPersona.Staff,
            TestPersona.Deactivated,
            TestPersona.CrossOrg,
            TestPersona.StudentDevice,
            TestPersona.UnenrolledStudent,
            TestPersona.Anon,
        };

    public static TheoryData<TestPersona> AllPersonas() =>
        new()
        {
            TestPersona.Admin,
            TestPersona.Staff,
            TestPersona.Deactivated,
            TestPersona.CrossOrg,
            TestPersona.StudentDevice,
            TestPersona.UnenrolledStudent,
            TestPersona.Anon,
        };

    [SupabaseFact]
    public async Task AdminCanReadStudentJoinCode()
    {
        using HttpResponseMessage response = await fixture.RestAsync(
            TestPersona.Admin, HttpMethod.Post, "rpc/admin_student_join_code");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            fixture.StudentJoinCode,
            JsonNode.Parse(await response.Content.ReadAsStringAsync())!.GetValue<string>());
    }

    [SupabaseTheory]
    [MemberData(nameof(NonAdminPersonas))]
    public async Task NonAdminCannotReadStudentJoinCode(TestPersona persona)
    {
        using HttpResponseMessage response = await fixture.RestAsync(
            persona, HttpMethod.Post, "rpc/admin_student_join_code");
        string payload = await response.Content.ReadAsStringAsync();

        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("42501", payload, StringComparison.Ordinal);
    }

    [SupabaseTheory]
    [MemberData(nameof(AllPersonas))]
    public async Task JoinCodeTableIsUnreadableOverRest(TestPersona persona)
    {
        using HttpResponseMessage response = await fixture.RestAsync(
            persona, HttpMethod.Get, "organization_join_codes?select=*");

        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("permission denied", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [SupabaseTheory]
    [MemberData(nameof(AllPersonas))]
    public async Task OrganizationsNeverExposeJoinCode(TestPersona persona)
    {
        using HttpResponseMessage response = await fixture.RestAsync(
            persona, HttpMethod.Get, "organizations?select=*");
        string payload = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("student_join_code", payload, StringComparison.Ordinal);
    }

    [SupabaseFact]
    public async Task RotationInvalidatesOldCodeButKeepsEnrolledDeviceActive()
    {
        string originalCode = fixture.StudentJoinCode;
        string rotatedCode = string.Empty;
        try
        {
            using (HttpResponseMessage rotate = await fixture.RestAsync(
                       TestPersona.Admin, HttpMethod.Post, "rpc/rotate_student_join_code"))
            {
                Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
                rotatedCode = JsonNode.Parse(await rotate.Content.ReadAsStringAsync())!.GetValue<string>();
                Assert.NotEqual(originalCode, rotatedCode);
            }

            using (HttpResponseMessage oldEnrollment = await fixture.RestAsync(
                       TestPersona.UnenrolledStudent,
                       HttpMethod.Post,
                       "rpc/enroll_student_device",
                       new JsonObject { ["join_code"] = originalCode }))
            {
                Assert.Equal(HttpStatusCode.Forbidden, oldEnrollment.StatusCode);
                Assert.Contains("42501", await oldEnrollment.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            using (HttpResponseMessage newEnrollment = await fixture.RestAsync(
                       TestPersona.UnenrolledStudent,
                       HttpMethod.Post,
                       "rpc/enroll_student_device",
                       new JsonObject { ["join_code"] = rotatedCode }))
            {
                Assert.Equal(HttpStatusCode.OK, newEnrollment.StatusCode);
            }

            using HttpResponseMessage schedule = await fixture.RestAsync(
                TestPersona.StudentDevice,
                HttpMethod.Get,
                $"timetables?id=eq.{SupabaseFixture.SeedTimetableId}&select=id");
            Assert.Single((await SupabaseFixture.RowsAsync(schedule))!);

            string auditPayload = (await fixture.SqlScalarAsync<string>(
                """
                select coalesce(before::text, '') || coalesce(after::text, '')
                from public.audit_log
                where org_id = $1 and entity_type = 'organization_join_code'
                order by id desc
                limit 1
                """,
                SupabaseFixture.OrgAId))!;
            Assert.DoesNotContain(originalCode, auditPayload, StringComparison.Ordinal);
            Assert.DoesNotContain(rotatedCode, auditPayload, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.SqlAsync(
                "update public.organization_join_codes set code = $1, rotated_at = now(), rotated_by = null where org_id = $2",
                originalCode,
                SupabaseFixture.OrgAId);
            await fixture.SqlAsync(
                "delete from public.student_devices where user_id = $1",
                fixture.UnenrolledStudentUserId);
        }
    }

    [SupabaseFact]
    public async Task AdminCanRevokeAllStudentDevices()
    {
        try
        {
            using HttpResponseMessage revoke = await fixture.RestAsync(
                TestPersona.Admin, HttpMethod.Post, "rpc/revoke_student_devices");
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
            Assert.True(JsonNode.Parse(await revoke.Content.ReadAsStringAsync())!.GetValue<int>() >= 1);

            using HttpResponseMessage schedule = await fixture.RestAsync(
                TestPersona.StudentDevice, HttpMethod.Get, "timetables?select=id");
            Assert.Empty((await SupabaseFixture.RowsAsync(schedule))!);

            string? entityType = await fixture.SqlScalarAsync<string>(
                """
                select entity_type
                from public.audit_log
                where org_id = $1
                  and after ? 'revoked_device_count'
                order by id desc
                limit 1
                """,
                SupabaseFixture.OrgAId);
            Assert.Equal("student_devices", entityType);
        }
        finally
        {
            await fixture.SqlAsync(
                "insert into public.student_devices (user_id, org_id) values ($1, $2) on conflict (user_id) do update set org_id = excluded.org_id, last_seen_at = now()",
                fixture.StudentDeviceUserId,
                SupabaseFixture.OrgAId);
        }
    }

    [SupabaseFact]
    public async Task RotationFailsWhenJoinCodeRowIsMissing()
    {
        string originalCode = fixture.StudentJoinCode;
        try
        {
            await fixture.SqlAsync(
                "delete from public.organization_join_codes where org_id = $1",
                SupabaseFixture.OrgAId);

            using HttpResponseMessage response = await fixture.RestAsync(
                TestPersona.Admin, HttpMethod.Post, "rpc/rotate_student_join_code");
            string payload = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("42501", payload, StringComparison.Ordinal);
        }
        finally
        {
            await fixture.SqlAsync(
                """
                insert into public.organization_join_codes (org_id, code)
                values ($1, $2)
                on conflict (org_id) do update set
                    code = excluded.code,
                    rotated_at = now(),
                    rotated_by = null
                """,
                SupabaseFixture.OrgAId,
                originalCode);
        }
    }

    [SupabaseTheory]
    [MemberData(nameof(NonAdminPersonas))]
    public async Task NonAdminCannotRotateOrRevoke(TestPersona persona)
    {
        foreach (string rpc in (string[])["rotate_student_join_code", "revoke_student_devices"])
        {
            using HttpResponseMessage response = await fixture.RestAsync(
                persona, HttpMethod.Post, $"rpc/{rpc}");
            Assert.False(response.IsSuccessStatusCode);
            Assert.Contains("42501", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [SupabaseTheory]
    [InlineData("abcd efgh jkmn pqrs")]
    [InlineData("ABCD-EFGH-JKMN-PQRS")]
    [InlineData("abcdefghijklmnop")]
    public async Task EnrollmentNormalizesSpacesDashesAndLowercase(string template)
    {
        string formatted = FormatLike(template, fixture.StudentJoinCode);
        try
        {
            using HttpResponseMessage response = await fixture.RestAsync(
                TestPersona.UnenrolledStudent,
                HttpMethod.Post,
                "rpc/enroll_student_device",
                new JsonObject { ["join_code"] = formatted });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await fixture.SqlAsync(
                "delete from public.student_devices where user_id = $1",
                fixture.UnenrolledStudentUserId);
        }
    }

    [SupabaseFact]
    public async Task OrganizationInsertAutomaticallyAssignsJoinCode()
    {
        Guid orgId = Guid.NewGuid();
        try
        {
            await fixture.SqlAsync(
                "insert into public.organizations (id, name) values ($1, $2)",
                orgId,
                "Join-code trigger probe");

            string? code = await fixture.SqlScalarAsync<string>(
                "select code from public.organization_join_codes where org_id = $1",
                orgId);
            Assert.Matches("^[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{16}$", code!);
        }
        finally
        {
            await fixture.SqlAsync("delete from public.week_schedule where org_id = $1", orgId);
            await fixture.SqlAsync("delete from public.audit_log where org_id = $1", orgId);
            await fixture.SqlAsync("delete from public.organizations where id = $1", orgId);
        }
    }

    private static string FormatLike(string template, string code)
    {
        Queue<char> characters = new(code);
        return new string(template.Select(character =>
            char.IsLetterOrDigit(character) ? characters.Dequeue() : character).ToArray())
            .ToLowerInvariant();
    }
}
