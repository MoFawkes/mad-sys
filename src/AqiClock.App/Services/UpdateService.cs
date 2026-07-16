using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Velopack;
using Velopack.Sources;

namespace AqiClock.App.Services;

public sealed partial class UpdateService : IUpdateService
{
    private readonly AqiClockUpdateOptions _options;
    private readonly ILogger<UpdateService> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private UpdateManager? _manager;
    private Task? _loop;
    private bool _disposed;

    public UpdateService(IOptions<AqiClockUpdateOptions> options, ILogger<UpdateService> logger)
    {
        _options = options.Value;
        _logger = logger;
        Current = string.IsNullOrWhiteSpace(_options.RepositoryUrl)
            ? new(UpdateStatus.Disabled)
            : new(UpdateStatus.Checking);
    }

    public UpdateState Current { get; private set; }

    public event EventHandler<UpdateState>? StateChanged;

    public void Start()
    {
        if (_loop is not null || string.IsNullOrWhiteSpace(_options.RepositoryUrl)) return;
        _manager = new UpdateManager(new GithubSource(_options.RepositoryUrl, null, false));
        if (!_manager.IsInstalled) { SetState(new(UpdateStatus.Disabled)); return; }
        _loop = RunAsync(_lifetime.Token);
    }

    public void PrepareUpdateOnExit()
    {
        VelopackAsset? pending = _manager?.UpdatePendingRestart;
        if (pending is not null) _manager!.WaitExitThenApplyUpdates(pending, silent: true, restart: false);
    }

    private async Task RunAsync(CancellationToken token)
    {
        TimeSpan interval = TimeSpan.FromHours(Math.Max(1, _options.CheckIntervalHours));
        while (!token.IsCancellationRequested)
        {
            await CheckAsync(token).ConfigureAwait(false);
            try { await Task.Delay(interval, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    private async Task CheckAsync(CancellationToken token)
    {
        try
        {
            SetState(new(UpdateStatus.Checking));
            UpdateInfo? update = await _manager!.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null) { SetState(new(UpdateStatus.UpToDate)); return; }
            await _manager.DownloadUpdatesAsync(update, cancelToken: token).ConfigureAwait(false);
            SetState(new(UpdateStatus.Downloaded, update.TargetFullRelease.Version.ToString()));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            LogUpdateCheckFailed(_logger, exception);
            SetState(new(UpdateStatus.Failed));
        }
    }

    private void SetState(UpdateState state)
    {
        Current = state;
        StateChanged?.Invoke(this, state);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Update check or download failed")]
    private static partial void LogUpdateCheckFailed(ILogger logger, Exception exception);

    public void Dispose()
    {
        if (_disposed) return;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _disposed = true;
    }
}
