using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using CommunityToolkit.Mvvm.Messaging;
using AqiClock.Domain.Entities;

namespace AqiClock.Application.Services;

public sealed class SessionService : ISessionService, IRecipient<DataChanged>, IDisposable
{
    private readonly ISessionStore _sessionStore;
    private readonly ISupabaseGateway _gateway;
    private readonly IProfileRepository _profiles;
    private readonly ILocalCache _cache;
    private readonly IMessenger _messenger;
    private readonly IDeviceAudienceContext _audience;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private StoredSession? _storedSession;

    public SessionService(ISessionStore sessionStore, ISupabaseGateway gateway, IProfileRepository profiles, ILocalCache cache, IMessenger messenger)
        : this(sessionStore, gateway, profiles, cache, messenger, new DeviceAudienceContext(messenger))
    {
    }

    public SessionService(ISessionStore sessionStore, ISupabaseGateway gateway, IProfileRepository profiles, ILocalCache cache, IMessenger messenger, IDeviceAudienceContext audience)
    {
        _sessionStore = sessionStore;
        _gateway = gateway;
        _profiles = profiles;
        _cache = cache;
        _messenger = messenger;
        _audience = audience;
        messenger.Register(this);
    }

    public SessionState Current { get; private set; } = SessionState.SignedOut;

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        StoredSession? stored = await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            SetState(SessionState.SignedOut);
            return;
        }

        try
        {
            AuthenticatedSession session = await _gateway.RefreshSessionAsync(stored, cancellationToken).ConfigureAwait(false);
            await SaveAndSetStateAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            await _sessionStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            _storedSession = null;
            SetState(SessionState.ReauthenticationRequired);
        }
        catch (Exception exception) when (IsTransientRefreshFailure(exception, cancellationToken))
        {
            await _gateway.RestoreAccessTokenAsync(stored, cancellationToken).ConfigureAwait(false);
            _storedSession = stored;
            if (!await TrySetCachedStateAsync(stored, cancellationToken).ConfigureAwait(false))
                await RequireReauthenticationAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransientRefreshFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TimeoutException or IOException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;

    public async Task EnsureFreshAsync(CancellationToken cancellationToken = default)
    {
        StoredSession? stored = _storedSession ?? await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (stored?.ExpiresAt is not { } expiresAt || expiresAt > DateTimeOffset.UtcNow.AddMinutes(5)) return;
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            stored = _storedSession ?? await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (stored?.ExpiresAt is not { } currentExpiry || currentExpiry > DateTimeOffset.UtcNow.AddMinutes(5)) return;
            AuthenticatedSession refreshed = await _gateway.RefreshSessionAsync(stored, cancellationToken).ConfigureAwait(false);
            await SaveSessionAsync(refreshed, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            await RequireReauthenticationAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally { _refreshGate.Release(); }
    }

    public async Task RefreshProfileAsync(CancellationToken cancellationToken = default)
    {
        if (Current.UserId is not { } userId || Current.IsAnonymous) return;
        Profile? profile = await _profiles.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (Current.UserId != userId) return;

        _audience.SetTeacher(profile?.Role ?? UserRole.Teacher);
        SessionState candidate = Current with
        {
            Role = profile?.Role ?? UserRole.Teacher,
            IsActive = profile?.IsActive ?? false,
            RoleConfirmed = true,
        };
        if (candidate != Current) SetState(candidate);
    }

    public async Task SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        await _cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        AuthenticatedSession session = await _gateway.SignInAsync(email.Trim(), password, cancellationToken).ConfigureAwait(false);
        await SaveAndSetStateAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnrollStudentDeviceAsync(string joinCode, CancellationToken cancellationToken = default)
    {
        AuthenticatedSession session = await _gateway.SignInAnonymouslyAsync(cancellationToken).ConfigureAwait(false);
        try { await _gateway.EnrollStudentDeviceAsync(joinCode, cancellationToken).ConfigureAwait(false); }
        catch
        {
            await _gateway.SignOutAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        await _audience.SetStudentAsync([], [], cancellationToken).ConfigureAwait(false);
        await SaveAndSetStateAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _gateway.SignOutAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _sessionStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            _storedSession = null;
            await _audience.ClearAsync(CancellationToken.None).ConfigureAwait(false);
            SetState(SessionState.SignedOut);
        }
    }

    private async Task SaveAndSetStateAsync(AuthenticatedSession session, CancellationToken cancellationToken)
    {
        await SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
        Profile? profile = await _profiles.GetByIdAsync(session.UserId, cancellationToken).ConfigureAwait(false);
        if (session.IsAnonymous)
        {
            if (_audience.Current.Role != DeviceAudienceRole.StudentDevice)
                await _audience.SetStudentAsync([], [], cancellationToken).ConfigureAwait(false);
            SetState(new SessionState(session.UserId, null, null, true, false, true));
            return;
        }
        UserRole? initialRole = profile?.Role == UserRole.Admin ? UserRole.Teacher : profile?.Role;
        if (_audience.Current.Role == DeviceAudienceRole.StudentDevice)
            await _audience.ClearAsync(cancellationToken).ConfigureAwait(false);
        _audience.SetTeacher(initialRole ?? UserRole.Teacher);
        SetState(new SessionState(session.UserId, session.Email, initialRole, profile?.IsActive ?? false, false));
    }

    private async Task SaveSessionAsync(AuthenticatedSession session, CancellationToken cancellationToken)
    {
        _storedSession = new StoredSession(session.AccessToken, session.RefreshToken, session.ExpiresAt);
        await _sessionStore.SaveAsync(_storedSession, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TrySetCachedStateAsync(StoredSession stored, CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(stored.AccessToken, out Guid userId, out bool isAnonymous)) return false;
        Profile? profile = await _profiles.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (isAnonymous)
        {
            if (_audience.Current.Role != DeviceAudienceRole.StudentDevice)
                await _audience.SetStudentAsync([], [], cancellationToken).ConfigureAwait(false);
            SetState(new SessionState(userId, null, null, true, false, true));
            return true;
        }
        UserRole? role = profile?.Role == UserRole.Admin ? UserRole.Teacher : profile?.Role;
        if (_audience.Current.Role == DeviceAudienceRole.StudentDevice)
            await _audience.ClearAsync(cancellationToken).ConfigureAwait(false);
        _audience.SetTeacher(role ?? UserRole.Teacher);
        SetState(new SessionState(userId, null, role, profile?.IsActive ?? false, false));
        return true;
    }

    private static bool TryGetIdentity(string accessToken, out Guid userId, out bool isAnonymous)
    {
        userId = default; isAnonymous = false;
        try
        {
            string[] segments = accessToken.Split('.');
            if (segments.Length != 3) return false;
            string payload = segments[1].Replace('-', '+').Replace('_', '/').PadRight((segments[1].Length + 3) / 4 * 4, '=');
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!document.RootElement.TryGetProperty("sub", out System.Text.Json.JsonElement subject) || !Guid.TryParse(subject.GetString(), out userId)) return false;
            isAnonymous = document.RootElement.TryGetProperty("is_anonymous", out System.Text.Json.JsonElement anonymous) && anonymous.ValueKind == System.Text.Json.JsonValueKind.True;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task RequireReauthenticationAsync(CancellationToken cancellationToken)
    {
        await _sessionStore.ClearAsync(cancellationToken).ConfigureAwait(false);
        _storedSession = null;
        SetState(SessionState.ReauthenticationRequired);
    }

    private void SetState(SessionState state)
    {
        Current = state;
        _messenger.Send(new SessionChanged(state));
    }

    public void Receive(DataChanged message)
    {
        if (message.Table == CacheTable.Profiles && Current.UserId is not null) _ = RefreshCachedProfileAsync(Current.UserId.Value);
    }

    private async Task RefreshCachedProfileAsync(Guid userId)
    {
        if (Current.UserId == userId) await RefreshProfileAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _messenger.UnregisterAll(this);
        _refreshGate.Dispose();
    }
}
