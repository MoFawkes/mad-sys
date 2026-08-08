using AqiClock.Application.Abstractions;

namespace AqiClock.App.Services;

public sealed class UpdateRestartPrompt(IUpdateService updates, IWindowService windows) : IDisposable
{
    private string? _promptedVersion;
    private bool _started;

    public void Start()
    {
        if (_started) return;
        _started = true;
        updates.StateChanged += OnStateChanged;
        Dispatch(updates.Current);
    }

    public void Handle(UpdateState state)
    {
        if (state.Status != UpdateStatus.Downloaded) return;
        string promptKey = state.TargetVersion ?? "pending";
        if (string.Equals(_promptedVersion, promptKey, StringComparison.Ordinal)) return;
        _promptedVersion = promptKey;

        string version = string.IsNullOrWhiteSpace(state.TargetVersion)
            ? "the downloaded update"
            : $"AQI Clock v{state.TargetVersion}";
        if (!windows.Confirm($"{version} is ready to install. Restart AQI Clock now?", "Update ready")) return;

        updates.RequestRestartToApply();
        windows.ShutdownApplication();
    }

    private void OnStateChanged(object? sender, UpdateState state) => Dispatch(state);

    private void Dispatch(UpdateState state)
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Handle(state);
        else _ = dispatcher.BeginInvoke(() => Handle(state));
    }

    public void Dispose()
    {
        updates.StateChanged -= OnStateChanged;
        GC.SuppressFinalize(this);
    }
}
