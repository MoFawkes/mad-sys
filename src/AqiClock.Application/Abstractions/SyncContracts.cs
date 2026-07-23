using AqiClock.Application.Sync;

namespace AqiClock.Application.Abstractions;

public interface ISyncService : IAsyncDisposable
{
    ConnectivityState State { get; }
    DateTimeOffset? LastSyncedAt { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SyncAllAsync(CancellationToken cancellationToken = default);
    Task SyncTableAsync(CacheTable table, CancellationToken cancellationToken = default);
    void SignalTableChanged(CacheTable table);
}
