using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Application.Services;
using AqiClock.Application.Sync;
using AqiClock.Infrastructure.Cache;
using AqiClock.Infrastructure.Supabase;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.IO;

namespace AqiClock.Application.Tests;

public sealed class InteractiveSignInSyncSmokeTests
{
    [WindowsSupabaseFact]
    public async Task PasswordSignInThenRealtimeSubscriptionAndInitialPullCompleteOnWindowsTfm()
    {
        string? url = Environment.GetEnvironmentVariable("SUPABASE_URL");
        string? anonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY");
        Assert.False(string.IsNullOrWhiteSpace(url));
        Assert.False(string.IsNullOrWhiteSpace(anonKey));

        string directory = Path.Combine(Path.GetTempPath(), "AqiClock.SignInSync", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var messenger = new WeakReferenceMessenger();
            using var database = new SqliteCacheDatabase(Path.Combine(directory, "cache.db"));
            using var gateway = new SupabaseGateway(Options.Create(new SupabaseOptions { Url = url, AnonKey = anonKey }), NullLogger<SupabaseGateway>.Instance);
            var profiles = new SqliteProfileRepository(database);
            var session = new SessionService(new MemorySessionStore(), gateway, profiles, database, messenger);
            await using var sync = new SyncService(gateway, database, messenger, new DebouncePolicy(TimeSpan.FromMilliseconds(10)), TimeProvider.System, NullLogger<SyncService>.Instance);

            await session.SignInAsync("aqitest-admin1@example.invalid", "Aq1-test-password!");
            await sync.StartAsync().WaitAsync(TimeSpan.FromSeconds(20));

            Assert.Equal(ConnectivityState.Online, sync.State);
            Assert.NotEmpty(await profiles.GetAllAsync());

            Guid organizationId = await gateway.GetCurrentOrganizationIdAsync();
            Guid timetableId = Guid.NewGuid();
            var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var recipient = new object();
            messenger.Register<AqiClock.Application.Messages.DataChanged>(recipient, (_, message) =>
            {
                if (message.Table == CacheTable.Timetables) refreshed.TrySetResult();
            });
            try
            {
                await gateway.InsertAsync(CacheTable.Timetables, new TimetableRow(timetableId, organizationId, "Windows TFM Realtime smoke", false));
                await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Contains(await new SqliteTimetableRepository(database).GetAllAsync(), item => item.Id == timetableId);
            }
            finally { await gateway.DeleteAsync(CacheTable.Timetables, timetableId); }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class MemorySessionStore : ISessionStore
    {
        private StoredSession? _session;
        public Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_session);
        public Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default) { _session = session; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { _session = null; return Task.CompletedTask; }
    }
}

public sealed class WindowsSupabaseFactAttribute : FactAttribute
{
    public WindowsSupabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUPABASE_URL")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY")))
            Skip = "Requires a running local Supabase stack.";
    }
}
