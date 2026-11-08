using System.Text.Json.Nodes;

namespace AqiClock.SupabaseTests;

[Collection("supabase")]
public sealed class RecurringReminderRlsTests(SupabaseFixture fixture)
{
    [SupabaseTheory]
    [InlineData(TestPersona.Admin, true, true)]
    [InlineData(TestPersona.Staff, true, false)]
    [InlineData(TestPersona.Deactivated, false, false)]
    [InlineData(TestPersona.CrossOrg, false, false)]
    [InlineData(TestPersona.StudentDevice, false, false)]
    [InlineData(TestPersona.UnenrolledStudent, false, false)]
    [InlineData(TestPersona.Anon, false, false)]
    public async Task OnlyOrgStaffCanReadAndOnlyOrgAdminsCanWrite(TestPersona persona, bool canRead, bool canWrite)
    {
        Guid id = Guid.NewGuid();
        Guid insertId = Guid.NewGuid();
        await fixture.SqlAsync("insert into public.recurring_reminders(id,org_id,title,body,weekdays,timing) values ($1,$2,'Chart','Fill chart',16,'lesson_end')", id, SupabaseFixture.OrgAId);
        try
        {
            using HttpResponseMessage read = await fixture.RestAsync(persona, HttpMethod.Get, $"recurring_reminders?id=eq.{id}");
            JsonArray? visible = await SupabaseFixture.RowsAsync(read);
            Assert.Equal(canRead ? 1 : 0, visible?.Count ?? 0);

            using HttpResponseMessage insert = await fixture.RestAsync(persona, HttpMethod.Post, "recurring_reminders", Body(insertId, SupabaseFixture.OrgAId));
            JsonArray? inserted = await SupabaseFixture.RowsAsync(insert);
            Assert.Equal(canWrite ? 1 : 0, inserted?.Count ?? 0);
            Assert.Equal(canWrite ? 1L : 0L, await fixture.SqlScalarAsync<long>("select count(*) from public.recurring_reminders where id=$1", insertId));

            using HttpResponseMessage update = await fixture.RestAsync(persona, HttpMethod.Patch, $"recurring_reminders?id=eq.{id}", new JsonObject { ["title"] = "Changed" });
            JsonArray? updated = await SupabaseFixture.RowsAsync(update);
            Assert.Equal(canWrite ? 1 : 0, updated?.Count ?? 0);
            Assert.Equal(canWrite ? "Changed" : "Chart", await fixture.SqlScalarAsync<string>("select title from public.recurring_reminders where id=$1", id));

            using HttpResponseMessage delete = await fixture.RestAsync(persona, HttpMethod.Delete, $"recurring_reminders?id=eq.{id}");
            JsonArray? deleted = await SupabaseFixture.RowsAsync(delete);
            Assert.Equal(canWrite ? 1 : 0, deleted?.Count ?? 0);
            Assert.Equal(canWrite ? 0L : 1L, await fixture.SqlScalarAsync<long>("select count(*) from public.recurring_reminders where id=$1", id));
        }
        finally { await fixture.SqlAsync("delete from public.recurring_reminders where id=$1 or id=$2", id, insertId); }
    }

    [SupabaseFact]
    public async Task AdminCannotCreateOrMoveRemindersToAnotherOrganization()
    {
        Guid id = Guid.NewGuid();
        using HttpResponseMessage insert = await fixture.RestAsync(TestPersona.Admin, HttpMethod.Post, "recurring_reminders", Body(id, fixture.OrgBId));
        Assert.False(insert.IsSuccessStatusCode);
        await fixture.SqlAsync("insert into public.recurring_reminders(id,org_id,title,body,weekdays,timing) values ($1,$2,'Chart','Fill chart',16,'lesson_end')", id, SupabaseFixture.OrgAId);
        try
        {
            using HttpResponseMessage update = await fixture.RestAsync(TestPersona.Admin, HttpMethod.Patch,
                $"recurring_reminders?id=eq.{id}", new JsonObject { ["org_id"] = fixture.OrgBId.ToString() });
            Assert.False(update.IsSuccessStatusCode);
            Assert.Equal(SupabaseFixture.OrgAId, await fixture.SqlScalarAsync<Guid>("select org_id from public.recurring_reminders where id=$1", id));
        }
        finally { await fixture.SqlAsync("delete from public.recurring_reminders where id=$1", id); }
    }

    [SupabaseTheory]
    [InlineData(0, "lesson_end", null)]
    [InlineData(128, "lesson_end", null)]
    [InlineData(16, "fixed_time", null)]
    [InlineData(16, "lesson_end", "16:00")]
    [InlineData(16, "fixed_time", "24:00")]
    [InlineData(16, "fixed_time", "16:00:30")]
    public async Task InvalidRecurrenceIsRejected(int days, string timing, string? time)
    {
        JsonObject body = Body(Guid.NewGuid(), SupabaseFixture.OrgAId);
        body["weekdays"] = days; body["timing"] = timing; body["fixed_time"] = time;
        using HttpResponseMessage response = await fixture.RestAsync(TestPersona.Admin, HttpMethod.Post, "recurring_reminders", body);
        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("23514", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [SupabaseFact]
    public async Task FixedTimeCanBeClearedWhenSwitchingToLessonEndAndChangesAreAudited()
    {
        Guid id = Guid.NewGuid();
        JsonObject body = Body(id, SupabaseFixture.OrgAId);
        body["timing"] = "fixed_time"; body["fixed_time"] = "16:00";
        try
        {
            using HttpResponseMessage insert = await fixture.RestAsync(TestPersona.Admin, HttpMethod.Post, "recurring_reminders", body);
            Assert.True(insert.IsSuccessStatusCode, await insert.Content.ReadAsStringAsync());
            using HttpResponseMessage update = await fixture.RestAsync(TestPersona.Admin, HttpMethod.Patch, $"recurring_reminders?id=eq.{id}",
                new JsonObject { ["timing"] = "lesson_end", ["fixed_time"] = null, ["is_enabled"] = false });
            Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
            Assert.Equal(1L, await fixture.SqlScalarAsync<long>("select count(*) from public.recurring_reminders where id=$1 and fixed_time is null and not is_enabled", id));
            Assert.Equal(2L, await fixture.SqlScalarAsync<long>("select count(*) from public.audit_log where entity_id=$1 and actor_id=$2 and entity_type='recurring_reminders'", id, fixture.AdminUserId));
        }
        finally { await fixture.SqlAsync("delete from public.recurring_reminders where id=$1", id); }
    }

    private static JsonObject Body(Guid id, Guid org) => new()
    {
        ["id"] = id.ToString(), ["org_id"] = org.ToString(), ["title"] = "Chart", ["body"] = "Fill chart",
        ["weekdays"] = 16, ["timing"] = "lesson_end",
    };
}
