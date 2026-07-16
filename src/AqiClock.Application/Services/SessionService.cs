using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using CommunityToolkit.Mvvm.Messaging;
using AqiClock.Domain.Entities;

namespace AqiClock.Application.Services;

public sealed class SessionService : ISessionService, IRecipient<DataChanged>
{
    private readonly ISessionStore _sessionStore;
    private readonly ISupabaseGateway _gateway;
    private readonly IProfileRepository _profiles;
    private readonly ILocalCache _cache;
    private readonly IMessenger _messenger;

    public SessionService(ISessionStore sessionStore, ISupabaseGateway gateway, IProfileRepository profiles, ILocalCache cache, IMessenger messenger)
    {
        _sessionStore = sessionStore;
        _gateway = gateway;
        _profiles = profiles;
        _cache = cache;
        _messenger = messenger;
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
        catch (Exception exception) when (exception is HttpRequestException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetState(SessionState.ReauthenticationRequired);
        }
    }

    public async Task SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        await _cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        AuthenticatedSession session = await _gateway.SignInAsync(email.Trim(), password, cancellationToken).ConfigureAwait(false);
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
            await _cache.WipeAsync(CancellationToken.None).ConfigureAwait(false);
            SetState(SessionState.SignedOut);
        }
    }

    private async Task SaveAndSetStateAsync(AuthenticatedSession session, CancellationToken cancellationToken)
    {
        await _sessionStore.SaveAsync(new StoredSession(session.AccessToken, session.RefreshToken, session.ExpiresAt), cancellationToken).ConfigureAwait(false);
        Profile? profile = await _profiles.GetByIdAsync(session.UserId, cancellationToken).ConfigureAwait(false);
        SetState(new SessionState(session.UserId, session.Email, profile?.Role, profile?.IsActive ?? false, false));
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
        Profile? profile = await _profiles.GetByIdAsync(userId).ConfigureAwait(false);
        if (profile is not null && Current.UserId == userId)
        {
            SetState(Current with { Role = profile.Role, IsActive = profile.IsActive });
        }
    }
}
