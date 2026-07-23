using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace AqiClock.Application.Services;

public sealed partial class SyncService : ISyncService, IRecipient<SessionChanged>
{
    private readonly ISupabaseGateway gateway;
    private readonly ILocalCache cache;
    private readonly IMessenger messenger;
    private readonly DebouncePolicy debouncePolicy;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SyncService> logger;
    private static readonly CacheTable[] Tables = Enum.GetValues<CacheTable>();
    private readonly ConcurrentDictionary<CacheTable, CancellationTokenSource> _debounces = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SemaphoreSlim _subscriptionGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly TimeSpan _heartbeatInterval;
    private readonly bool _usesDefaultHeartbeat;
    private CancellationTokenSource? _lifetime;
    private IRealtimeSubscription? _subscription;
    private Task? _heartbeatTask;
    private Task? _subscriptionTask;
    private int _failures;
    private bool _disposed;

    public SyncService(
        ISupabaseGateway gateway,
        ILocalCache cache,
        IMessenger messenger,
        DebouncePolicy debouncePolicy,
        TimeProvider timeProvider,
        ILogger<SyncService> logger,
        TimeSpan? heartbeatInterval = null)
    {
        this.gateway = gateway;
        this.cache = cache;
        this.messenger = messenger;
        this.debouncePolicy = debouncePolicy;
        this.timeProvider = timeProvider;
        this.logger = logger;
        _usesDefaultHeartbeat = heartbeatInterval is null;
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
        messenger.Register(this);
    }

    public ConnectivityState State { get; private set; } = ConnectivityState.Offline;
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lifetime is not null) return;
            await cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _failures = 0;
            _lifetime = new CancellationTokenSource();
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            _subscriptionTask = ObserveBackgroundAsync(EnsureRealtimeSubscribedAsync(_lifetime.Token), "realtime subscription");
            _heartbeatTask = ObserveBackgroundAsync(RunHeartbeatAsync(_lifetime.Token), "heartbeat");
            await SyncAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopCoreAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
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
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or IOException or InvalidOperationException)
        {
            _failures++;
            LogSyncCycleFailed(logger, exception);
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
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or IOException or InvalidOperationException)
        {
            _failures++;
            LogSyncCycleFailed(logger, exception);
            SetState(ConnectivityState.Offline);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sync cycle failed; staying offline until the next retry")]
    private static partial void LogSyncCycleFailed(ILogger logger, Exception exception);

    public void SignalTableChanged(CacheTable table)
    {
        CancellationToken lifetimeToken = _lifetime?.Token ?? CancellationToken.None;
        var next = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _debounces.AddOrUpdate(table, next, (_, previous) => { previous.Cancel(); previous.Dispose(); return next; });
        _ = DebounceAsync(table, next);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync().ConfigureAwait(false);
        _disposed = true;
        messenger.UnregisterAll(this);
        _lifecycleGate.Dispose();
        _subscriptionGate.Dispose();
        _syncGate.Dispose();
    }

    public void Receive(SessionChanged message)
    {
        if (message.State.UserId is null)
            _ = ObserveBackgroundAsync(StopAsync(), "signed-out sync stop");
    }

    private async Task StopCoreAsync()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        CancellationTokenSource? lifetime = _lifetime;
        _lifetime = null;
        if (lifetime is not null) await lifetime.CancelAsync().ConfigureAwait(false);

        foreach (KeyValuePair<CacheTable, CancellationTokenSource> pair in _debounces.ToArray())
        {
            if (_debounces.TryRemove(pair))
            {
                CancellationTokenSource source = pair.Value;
                source.Cancel();
                source.Dispose();
            }
        }

        Task[] backgroundTasks = [_subscriptionTask ?? Task.CompletedTask, _heartbeatTask ?? Task.CompletedTask];
        _subscriptionTask = null;
        _heartbeatTask = null;
        await Task.WhenAll(backgroundTasks).ConfigureAwait(false);

        await _subscriptionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_subscription is not null)
            {
                await _subscription.DisposeAsync().ConfigureAwait(false);
                _subscription = null;
            }
        }
        finally
        {
            _subscriptionGate.Release();
        }

        lifetime?.Dispose();
        _failures = 0;
        SetState(ConnectivityState.Offline);
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
            int delayStep = Math.Max(1, _failures);
            TimeSpan delay = BackoffPolicy.GetDelay(
                delayStep,
                _heartbeatInterval,
                _usesDefaultHeartbeat ? TimeSpan.FromMinutes(5) : TimeSpan.FromTicks(_heartbeatInterval.Ticks * 16));
            try
            {
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
                await EnsureRealtimeSubscribedAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await SyncAllAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _failures++;
                    SetState(ConnectivityState.Offline);
                    LogBackgroundSyncFailed(logger, "heartbeat refresh", exception);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task EnsureRealtimeSubscribedAsync(CancellationToken cancellationToken)
    {
        if (_subscription is not null) return;

        await _subscriptionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_subscription is not null) return;
            _subscription = await gateway.SubscribeAsync(OnRealtimeChangeAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogBackgroundSyncFailed(logger, "realtime subscription", exception);
        }
        finally
        {
            _subscriptionGate.Release();
        }
    }

    private Task OnRealtimeChangeAsync(TableChangeSignal signal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SignalTableChanged(signal.Table);
        return Task.CompletedTask;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => _ = ObserveBackgroundAsync(SyncAllAsync(_lifetime?.Token ?? CancellationToken.None), "network-change retry");

    private async Task ObserveBackgroundAsync(Task operation, string operationName)
    {
        try { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { LogBackgroundSyncFailed(logger, operationName, exception); }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Sync background operation {OperationName} failed")]
    private static partial void LogBackgroundSyncFailed(ILogger logger, string operationName, Exception exception);

    private void SetState(ConnectivityState state)
    {
        State = state;
        messenger.Send(new ConnectivityChanged(state, LastSyncedAt));
    }
}
