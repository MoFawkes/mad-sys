namespace AqiClock.Application.Abstractions;

public sealed record TableChangeSignal(CacheTable Table);

public interface IRealtimeSubscription : IAsyncDisposable;

public interface ISupabaseGateway
{
    Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default);
    Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default);
    Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default);
    Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default);
    Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default);
    Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default);
}
