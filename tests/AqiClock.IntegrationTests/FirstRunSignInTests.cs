using AqiClock.Application.Abstractions;
using AqiClock.Application.Services;
using AqiClock.Infrastructure.Cache;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.IntegrationTests;

public sealed class FirstRunSignInTests
{
    [Fact]
    public async Task SignInAgainstMissingCacheCreatesSchemaBeforeProfileLookup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AqiClockFirstRun", Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(directory, "cache.db");
        try
        {
            using var cache = new SqliteCacheDatabase(databasePath);
            var store = new SessionStoreStub();
            AuthenticatedSession authenticated = new(Guid.NewGuid(), "staff@example.test", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1));
            var service = new SessionService(store, new GatewayStub(authenticated), new SqliteProfileRepository(cache), cache, new WeakReferenceMessenger());

            await service.SignInAsync(authenticated.Email, "password");

            Assert.True(File.Exists(databasePath));
            Assert.Equal(authenticated.UserId, service.Current.UserId);
            Assert.Null(service.Current.Role);
            Assert.NotNull(store.Saved);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class SessionStoreStub : ISessionStore
    {
        public StoredSession? Saved { get; private set; }
        public Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<StoredSession?>(null);
        public Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default) { Saved = session; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class GatewayStub(AuthenticatedSession session) : ISupabaseGateway
    {
        public Task CompletePasswordRecoveryAsync(string accessToken, string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => Task.FromResult(session);
        public Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthenticatedSession> RefreshSessionAsync(StoredSession stored, CancellationToken cancellationToken = default) => Task.FromResult(session);
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
