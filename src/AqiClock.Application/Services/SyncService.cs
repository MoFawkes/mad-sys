using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.Application.Services;

public sealed class SyncService(
    ISupabaseGateway gateway,
    ILocalCache cache,
    IMessenger messenger,
    DebouncePolicy debouncePolicy,
    TimeProvider timeProvider) : ISyncService
{
    private static readonly CacheTable[] Tables = Enum.GetValues<CacheTable>();
    private readonly ConcurrentDictionary<CacheTable, CancellationTokenSource> _debounces = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private IRealtimeSubscription? _subscription;
    private int _failures;

    public ConnectivityState State { get; private set; } = ConnectivityState.Offline;
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_lifetime is not null) return;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        _subscription = await gateway.SubscribeAsync(OnRealtimeChangeAsync, _lifetime.Token).ConfigureAwait(false);
        _ = RunHeartbeatAsync(_lifetime.Token);
        await SyncAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetState(ConnectivityState.Syncing);
            Guid organizationId = await gateway.GetCurrentOrganizationIdAsync(cancellationToken).ConfigureAwait(false);
            string? cachedOrganization = await cache.GetMetaAsync("org_id", cancellationToken).ConfigureAwait(false);
            if (cachedOrganization is not null && !string.Equals(cachedOrganization, organizationId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                await cache.WipeAsync(cancellationToken).ConfigureAwait(false);
            }

            await cache.SetMetaAsync("org_id", organizationId.ToString(), cancellationToken).ConfigureAwait(false);
            foreach (CacheTable table in Tables)
            {
                await PullAndReplaceAsync(table, cancellationToken).ConfigureAwait(false);
            }

            _failures = 0;
            LastSyncedAt = timeProvider.GetUtcNow();
            SetState(ConnectivityState.Online);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or IOException)
        {
            _failures++;
            SetState(ConnectivityState.Offline);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task SyncTableAsync(CacheTable table, CancellationToken cancellationToken = default)
    {
        try
        {
            await PullAndReplaceAsync(table, cancellationToken).ConfigureAwait(false);
            _failures = 0;
            LastSyncedAt = timeProvider.GetUtcNow();
            SetState(ConnectivityState.Online);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or IOException)
        {
            _failures++;
            SetState(ConnectivityState.Offline);
        }
    }

    public void SignalTableChanged(CacheTable table)
    {
        CancellationToken lifetimeToken = _lifetime?.Token ?? CancellationToken.None;
        var next = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _debounces.AddOrUpdate(table, next, (_, previous) => { previous.Cancel(); previous.Dispose(); return next; });
        _ = DebounceAsync(table, next);
    }

    public async ValueTask DisposeAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        if (_lifetime is not null) await _lifetime.CancelAsync().ConfigureAwait(false);
        foreach (CancellationTokenSource source in _debounces.Values) source.Dispose();
        if (_subscription is not null) await _subscription.DisposeAsync().ConfigureAwait(false);
        _lifetime?.Dispose();
        _syncGate.Dispose();
    }

    private async Task PullAndReplaceAsync(CacheTable table, CancellationToken cancellationToken)
    {
        CacheSnapshot snapshot = await gateway.PullAsync(table, cancellationToken).ConfigureAwait(false);
        await cache.ReplaceSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
        messenger.Send(new DataChanged(table));
    }

    private async Task DebounceAsync(CacheTable table, CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(debouncePolicy.Delay, timeProvider, source.Token).ConfigureAwait(false);
            await SyncTableAsync(table, source.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
        }
        finally
        {
            _debounces.TryRemove(new KeyValuePair<CacheTable, CancellationTokenSource>(table, source));
            source.Dispose();
        }
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delay = _failures == 0 ? TimeSpan.FromSeconds(30) : BackoffPolicy.GetDelay(_failures);
            try
            {
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
                await SyncAllAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private Task OnRealtimeChangeAsync(TableChangeSignal signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SignalTableChanged(signal.Table);
        return Task.CompletedTask;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => _ = SyncAllAsync(_lifetime?.Token ?? CancellationToken.None);

    private void SetState(ConnectivityState state)
    {
        State = state;
        messenger.Send(new ConnectivityChanged(state, LastSyncedAt));
    }
}
