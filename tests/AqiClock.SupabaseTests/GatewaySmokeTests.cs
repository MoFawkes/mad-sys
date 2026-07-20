using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Infrastructure.Supabase;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

        await gateway.UpdateWeekScheduleAsync(0, null);
        Assert.Null((await gateway.PullAsync(CacheTable.WeekSchedule)).Rows.Cast<WeekScheduleRow>().Single(x => x.Weekday == 0).TimetableId);

        await gateway.UpdateWeekScheduleAsync(0, monday.TimetableId);
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
