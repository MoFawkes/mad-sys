using AqiClock.Application.Abstractions;
using AqiClock.Application.Sync;

namespace AqiClock.Application.Messages;

public sealed record DataChanged(CacheTable Table);
public sealed record ConnectivityChanged(ConnectivityState State, DateTimeOffset? LastSyncedAt);
public sealed record SessionChanged(SessionState State);
