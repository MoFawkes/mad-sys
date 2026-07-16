namespace AqiClock.Application.Abstractions;

using System.Text.Json.Nodes;

public sealed record TableChangeSignal(CacheTable Table);

public interface IRealtimeSubscription : IAsyncDisposable;

public interface ISupabaseGateway
{
    Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default);
    Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default);
    Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default);
    Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default);
    Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default);
    Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default);
}

public sealed record AuditEntry(long Id, Guid? ActorId, string Action, string EntityType, Guid EntityId, JsonObject? Before, JsonObject? After, DateTimeOffset CreatedAt);

public class ServerWriteException(string message, string? serverCode, Exception? innerException = null) : Exception(message, innerException)
{
    public string? ServerCode { get; } = serverCode;
}
public sealed class ServerDeniedException(string message, string? serverCode = null) : ServerWriteException(message, serverCode);
public sealed class ReferencedRowException(string message) : ServerWriteException(message, "23503");
public sealed class LastAdminException(string message) : ServerWriteException(message, "23514");
public sealed class DuplicateRowException(string message) : ServerWriteException(message, "23505");
