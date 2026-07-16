using AqiClock.Domain.Entities;

namespace AqiClock.Application.Abstractions;

public sealed record StoredSession(string AccessToken, string RefreshToken, DateTimeOffset? ExpiresAt);

public sealed record AuthenticatedSession(Guid UserId, string Email, string AccessToken, string RefreshToken, DateTimeOffset? ExpiresAt);

public sealed record SessionState(Guid? UserId, string? Email, UserRole? Role, bool IsActive, bool RequiresSignIn)
{
    public static SessionState SignedOut { get; } = new(null, null, null, false, false);
    public static SessionState ReauthenticationRequired { get; } = new(null, null, null, false, true);
}

public interface ISessionStore
{
    Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface ISessionService
{
    SessionState Current { get; }
    Task RestoreAsync(CancellationToken cancellationToken = default);
    Task SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
