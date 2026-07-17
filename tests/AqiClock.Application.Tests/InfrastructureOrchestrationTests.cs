using AqiClock.Application.Abstractions;
using AqiClock.Application.Services;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.Application.Tests;

public sealed class InfrastructureOrchestrationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 300)]
    [InlineData(20, 300)]
    public void BackoffIsExponentialAndCapped(int failures, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), BackoffPolicy.GetDelay(failures));

    [Fact]
    public async Task SessionRestoreKeepsCacheWhenRefreshRequiresSignIn()
    {
        var cache = new FakeCache();
        var store = new FakeSessionStore { Session = new StoredSession("old", "expired", DateTimeOffset.UtcNow.AddDays(-1)) };
        var gateway = new FakeGateway { RefreshException = new UnauthorizedAccessException() };
        var service = new SessionService(store, gateway, new FakeProfiles(), cache, new WeakReferenceMessenger());

        await service.RestoreAsync();

        Assert.True(service.Current.RequiresSignIn);
        Assert.Equal(0, cache.WipeCount);
    }

    [Fact]
    public async Task SessionRestoreSourcesRoleFromCachedProfile()
    {
        Guid userId = Guid.NewGuid();
        var store = new FakeSessionStore { Session = new StoredSession("old", "refresh", null) };
        var gateway = new FakeGateway { RefreshedSession = new AuthenticatedSession(userId, "admin@example.test", "new", "new-refresh", DateTimeOffset.UtcNow.AddHours(1)) };
        var service = new SessionService(store, gateway, new FakeProfiles(new Profile(userId, "Admin", UserRole.Admin, true)), new FakeCache(), new WeakReferenceMessenger());

        await service.RestoreAsync();

        Assert.Equal(UserRole.Admin, service.Current.Role);
        Assert.Equal("new", store.Session?.AccessToken);
    }

    [Fact]
    public async Task SyncWipesCacheOnOrganizationChangeThenRepopulates()
    {
        var cache = new FakeCache();
        cache.Meta["org_id"] = Guid.NewGuid().ToString();
        var gateway = new FakeGateway { OrganizationId = Guid.NewGuid() };
        await using var service = new SyncService(gateway, cache, new WeakReferenceMessenger(), new DebouncePolicy(TimeSpan.Zero), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance);

        await service.SyncAllAsync();

        Assert.Equal(1, cache.WipeCount);
        Assert.Equal(Enum.GetValues<CacheTable>().Length, cache.Replaced.Count);
        Assert.Equal(ConnectivityState.Online, service.State);
    }

    [Fact]
    public async Task RealtimeSignalsAreDebouncedPerTable()
    {
        var cache = new FakeCache();
        var gateway = new FakeGateway();
        await using var service = new SyncService(gateway, cache, new WeakReferenceMessenger(), new DebouncePolicy(TimeSpan.FromMilliseconds(40)), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance);
        service.SignalTableChanged(CacheTable.Timetables);
        service.SignalTableChanged(CacheTable.Timetables);
        service.SignalTableChanged(CacheTable.Timetables);

        await Task.Delay(150);

        Assert.Equal(1, gateway.PullCounts.GetValueOrDefault(CacheTable.Timetables));
    }

    private sealed class FakeSessionStore : ISessionStore
    {
        public StoredSession? Session { get; set; }
        public Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Session);
        public Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default) { Session = session; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { Session = null; return Task.CompletedTask; }
    }

    private sealed class FakeProfiles(Profile? profile = null) : IProfileRepository
    {
        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Profile>>(profile is null ? [] : [profile]);
        public Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(profile?.Id == id ? profile : null);
    }

    private sealed class FakeCache : ILocalCache
    {
        public Dictionary<string, string> Meta { get; } = [];
        public List<CacheSnapshot> Replaced { get; } = [];
        public int WipeCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WipeAsync(CancellationToken cancellationToken = default) { WipeCount++; Meta.Clear(); Replaced.Clear(); return Task.CompletedTask; }
        public Task ReplaceSnapshotAsync(CacheSnapshot snapshot, CancellationToken cancellationToken = default) { Replaced.Add(snapshot); return Task.CompletedTask; }
        public Task<string?> GetMetaAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Meta.GetValueOrDefault(key));
        public Task SetMetaAsync(string key, string value, CancellationToken cancellationToken = default) { Meta[key] = value; return Task.CompletedTask; }
        public Task<DateTimeOffset?> GetLastSyncedAtAsync(CacheTable table, CancellationToken cancellationToken = default) => Task.FromResult<DateTimeOffset?>(null);
    }

    private sealed class FakeGateway : ISupabaseGateway
    {
        public Task CompletePasswordRecoveryAsync(string accessToken, string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Guid OrganizationId { get; init; } = Guid.NewGuid();
        public Exception? RefreshException { get; init; }
        public AuthenticatedSession RefreshedSession { get; init; } = new(Guid.NewGuid(), "staff@example.test", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Dictionary<CacheTable, int> PullCounts { get; } = [];
        public Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => Task.FromResult(RefreshedSession);
        public Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default) => RefreshException is null ? Task.FromResult(RefreshedSession) : Task.FromException<AuthenticatedSession>(RefreshException);
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(OrganizationId);
        public Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default) { PullCounts[table] = PullCounts.GetValueOrDefault(table) + 1; return Task.FromResult(new CacheSnapshot(table, [], DateTimeOffset.UtcNow)); }
        public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
        public Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default) => Task.FromResult<IRealtimeSubscription>(new FakeSubscription());
    }

    private sealed class FakeSubscription : IRealtimeSubscription
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
