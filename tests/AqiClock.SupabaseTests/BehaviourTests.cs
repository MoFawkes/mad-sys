using System.Net;
using System.Text.Json.Nodes;
using Npgsql;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class BehaviourTests(SupabaseFixture fixture)
{
    [SupabaseFact]
    public async Task AuthTriggerCreatesProfileFromEmailLocalPart()
    {
        string email = $"aqitest-autoprofile-{fixture.RunId}@example.invalid";
        Guid userId = await fixture.CreateUserAsync(email);

        string? displayName = await fixture.SqlScalarAsync<string>(
            "select display_name from public.profiles where id = $1", userId);

        Assert.Equal($"aqitest-autoprofile-{fixture.RunId}", displayName);
    }

    [SupabaseFact]
    public async Task AuthTriggerHonoursDisplayNameMetadata()
    {
        string email = $"aqitest-metadata-{fixture.RunId}@example.invalid";
        Guid userId = await fixture.CreateUserWithMetadataAsync(email, "Metadata Name");

        string? displayName = await fixture.SqlScalarAsync<string>(
            "select display_name from public.profiles where id = $1", userId);

        Assert.Equal("Metadata Name", displayName);
    }

    [SupabaseFact]
    public async Task StaffMayChangeOnlyOwnDisplayName()
    {
        using HttpResponseMessage ownName = await fixture.RestAsync(
            TestPersona.Staff,
            HttpMethod.Patch,
            $"profiles?id=eq.{fixture.StaffUserId}",
            new JsonObject { ["display_name"] = "Staff Display Name" });
        Assert.Single((await SupabaseFixture.RowsAsync(ownName))!);

        using HttpResponseMessage ownRole = await fixture.RestAsync(
            TestPersona.Staff,
            HttpMethod.Patch,
            $"profiles?id=eq.{fixture.StaffUserId}",
            new JsonObject { ["role"] = "admin" });
        Assert.Equal(HttpStatusCode.Forbidden, ownRole.StatusCode);
        Assert.Contains("42501", await ownRole.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using HttpResponseMessage anotherProfile = await fixture.RestAsync(
            TestPersona.Staff,
            HttpMethod.Patch,
            $"profiles?id=eq.{fixture.AdminUserId}",
            new JsonObject { ["display_name"] = "Forbidden" });
        Assert.Empty((await SupabaseFixture.RowsAsync(anotherProfile))!);
    }

    [SupabaseFact]
    public async Task LastAdminGuardCoversDemotionDeactivationAndDelete()
    {
        Guid orgId = Guid.NewGuid();
        await fixture.SqlAsync(
            "insert into public.organizations (id, name) values ($1, 'Last-admin test org')", orgId);

        Guid firstAdmin = await fixture.CreateUserAsync($"aqitest-lastadmin1-{fixture.RunId}@example.invalid");
        await fixture.SqlAsync(
            "update public.profiles set org_id = $1, role = 'admin' where id = $2", orgId, firstAdmin);

        PostgresException demotion = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.SqlAsync("update public.profiles set role = 'staff' where id = $1", firstAdmin));
        Assert.Equal("23514", demotion.SqlState);

        PostgresException deactivation = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.SqlAsync("update public.profiles set is_active = false where id = $1", firstAdmin));
        Assert.Equal("23514", deactivation.SqlState);

        PostgresException deletion = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.SqlAsync("delete from auth.users where id = $1", firstAdmin));
        Assert.Equal("23514", deletion.SqlState);

        Guid secondAdmin = await fixture.CreateUserAsync($"aqitest-lastadmin2-{fixture.RunId}@example.invalid");
        await fixture.SqlAsync(
            "update public.profiles set org_id = $1, role = 'admin' where id = $2", orgId, secondAdmin);

        await fixture.SqlAsync("update public.profiles set role = 'staff' where id = $1", firstAdmin);
        Assert.Equal("staff", await fixture.SqlScalarAsync<string>(
            "select role from public.profiles where id = $1", firstAdmin));

        PostgresException secondDeletion = await Assert.ThrowsAsync<PostgresException>(() =>
            fixture.SqlAsync("delete from auth.users where id = $1", secondAdmin));
        Assert.Equal("23514", secondDeletion.SqlState);
    }

    [SupabaseFact]
    public async Task ExistingJwtIsLockedOutImmediatelyAfterDeactivation()
    {
        using HttpResponseMessage read = await fixture.RestAsync(
            TestPersona.Deactivated, HttpMethod.Get, "timetables?select=id");
        Assert.Empty((await SupabaseFixture.RowsAsync(read))!);

        using HttpResponseMessage write = await fixture.RestAsync(
            TestPersona.Deactivated,
            HttpMethod.Post,
            "timetables",
            new JsonObject
            {
                ["org_id"] = SupabaseFixture.OrgAId.ToString(),
                ["name"] = $"Locked out {fixture.RunId}",
            });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [SupabaseFact]
    public async Task AuditCapturesActorActionsAndImages()
    {
        Guid timetableId = Guid.NewGuid();
        using HttpResponseMessage insert = await fixture.RestAsync(
            TestPersona.Admin,
            HttpMethod.Post,
            "timetables",
            new JsonObject
            {
                ["id"] = timetableId.ToString(),
                ["org_id"] = SupabaseFixture.OrgAId.ToString(),
                ["name"] = $"Audit {fixture.RunId}",
            });
        Assert.True(insert.IsSuccessStatusCode);

        using HttpResponseMessage update = await fixture.RestAsync(
            TestPersona.Admin,
            HttpMethod.Patch,
            $"timetables?id=eq.{timetableId}",
            new JsonObject { ["name"] = $"Audit updated {fixture.RunId}" });
        Assert.True(update.IsSuccessStatusCode);

        using HttpResponseMessage delete = await fixture.RestAsync(
            TestPersona.Admin, HttpMethod.Delete, $"timetables?id=eq.{timetableId}");
        Assert.True(delete.IsSuccessStatusCode);

        Assert.Equal(3L, await fixture.SqlScalarAsync<long>(
            "select count(*) from public.audit_log where entity_type = 'timetables' and entity_id = $1 and actor_id = $2",
            timetableId, fixture.AdminUserId));
        Assert.True(await fixture.SqlScalarAsync<bool>(
            "select before is null and after is not null from public.audit_log where entity_id = $1 and action = 'insert'",
            timetableId));
        Assert.True(await fixture.SqlScalarAsync<bool>(
            "select before is not null and after is not null from public.audit_log where entity_id = $1 and action = 'update'",
            timetableId));
        Assert.True(await fixture.SqlScalarAsync<bool>(
            "select before is not null and after is null from public.audit_log where entity_id = $1 and action = 'delete'",
            timetableId));
    }

    [SupabaseFact]
    public async Task ForeignKeyRestrictsReferencedTimetableThenPeriodsCascade()
    {
        Guid timetableId = Guid.NewGuid();
        Guid periodId = Guid.NewGuid();
        await fixture.SqlAsync(
            "insert into public.timetables (id, org_id, name) values ($1, $2, $3)",
            timetableId, SupabaseFixture.OrgAId, $"FK {fixture.RunId}");
        await fixture.SqlAsync(
            "insert into public.periods (id, timetable_id, name, start_time, end_time, sort_order) values ($1, $2, 'Cascade period', '14:00', '14:30', 1)",
            periodId, timetableId);

        using HttpResponseMessage assign = await fixture.RestAsync(
            TestPersona.Admin,
            HttpMethod.Patch,
            $"week_schedule?id=eq.{SupabaseFixture.SeedWeekdayMondayId}",
            new JsonObject { ["timetable_id"] = timetableId.ToString() });
        Assert.True(assign.IsSuccessStatusCode);

        using HttpResponseMessage restrictedDelete = await fixture.RestAsync(
            TestPersona.Admin, HttpMethod.Delete, $"timetables?id=eq.{timetableId}");
        Assert.Equal(HttpStatusCode.Conflict, restrictedDelete.StatusCode);

        using HttpResponseMessage unassign = await fixture.RestAsync(
            TestPersona.Admin,
            HttpMethod.Patch,
            $"week_schedule?id=eq.{SupabaseFixture.SeedWeekdayMondayId}",
            new JsonObject { ["timetable_id"] = null });
        Assert.True(unassign.IsSuccessStatusCode);

        using HttpResponseMessage allowedDelete = await fixture.RestAsync(
            TestPersona.Admin, HttpMethod.Delete, $"timetables?id=eq.{timetableId}");
        Assert.True(allowedDelete.IsSuccessStatusCode);
        Assert.Equal(0L, await fixture.SqlScalarAsync<long>(
            "select count(*) from public.periods where id = $1", periodId));
        Assert.Equal(1L, await fixture.SqlScalarAsync<long>(
            "select count(*) from public.audit_log where entity_type = 'periods' and entity_id = $1 and action = 'delete' and org_id = $2",
            periodId, SupabaseFixture.OrgAId));
    }

    [SupabaseFact]
    public async Task RealtimePublicationContainsExactlyTheIntendedTables()
    {
        string? tables = await fixture.SqlScalarAsync<string>(
            "select string_agg(tablename, ',' order by tablename) from pg_publication_tables where pubname = 'supabase_realtime' and schemaname = 'public'");

        Assert.Equal(
            "announcements,date_overrides,periods,profiles,timetables,week_schedule",
            tables);
    }
}
