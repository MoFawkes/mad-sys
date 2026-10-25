using System.IO;
using System.Security;
using System.Windows.Threading;
using AqiClock.App.ViewModels;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace AqiClock.App.Services;

public interface ITaskbarDisplayService
{
    bool IsAvailable { get; }
    string Status { get; }
    event EventHandler? StatusChanged;
    bool Configure(bool enabled);
}

public sealed partial class TaskbarDisplayService : ITaskbarDisplayService, IRecipient<SessionChanged>, IRecipient<AudienceChanged>, IDisposable
{
    private readonly ClockViewModel _clock;
    private readonly ISettingsService _settings;
    private readonly ISessionService _session;
    private readonly IDeviceAudienceContext _audience;
    private readonly ILogger<TaskbarDisplayService> _logger;
    private readonly IMessenger _messenger;
    private readonly DispatcherTimer _timer;
    private TaskbarSnapshotPublisher? _publisher;
    private bool _disposed;
    public bool IsAvailable => TaskbarRegistration.IsSupported && File.Exists(TaskbarRegistration.SourcePath);
    public string Status { get; private set; } = TaskbarRegistration.IsSupported
        ? "Enable to show the current lesson and countdown in the taskbar."
        : "Taskbar display is available on 64-bit Windows 10. Compact mode is available on this PC.";
    public event EventHandler? StatusChanged;

    public TaskbarDisplayService(ClockViewModel clock, ISettingsService settings, ISessionService session,
        IDeviceAudienceContext audience, IMessenger messenger, ILogger<TaskbarDisplayService> logger)
    {
        _clock = clock; _settings = settings; _session = session; _audience = audience; _messenger = messenger; _logger = logger;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        messenger.RegisterAll(this);
    }

    public void Receive(SessionChanged message) => RefreshAfterAccountChange();
    public void Receive(AudienceChanged message) => RefreshAfterAccountChange();
    private void RefreshAfterAccountChange()
    {
        if (_timer.Dispatcher.CheckAccess()) OnTick(this, EventArgs.Empty);
        else _ = _timer.Dispatcher.BeginInvoke(() => { if (!_disposed) OnTick(this, EventArgs.Empty); });
    }

    public void Start()
    {
        if (!TaskbarRegistration.IsSupported) return;
        if (!IsAvailable) { SetStatus("The taskbar component is missing from this build."); return; }
        // Refresh the launcher after an app update even when the toolbar is disabled.
        try { TaskbarRegistration.UpdateLaunchPath(); }
        catch (Exception error) when (IsConfigurationError(error)) { LogUnavailable(_logger, error); }
        if (_settings.Current.TaskbarDisplayEnabled) Apply(enabled: true, changeVisibility: false);
    }

    public bool Configure(bool enabled)
    {
        if (!IsAvailable) return !enabled;
        return Apply(enabled, changeVisibility: true);
    }

    private bool Apply(bool enabled, bool changeVisibility)
    {
        try
        {
            if (enabled)
            {
                TaskbarRegistration.Register();
                _publisher ??= new TaskbarSnapshotPublisher();
                Publish();
                _timer.Start();
            }
            // Ask Explorer to remove its band before removing COM registration.
            bool shown = TaskbarRegistration.NotifyShell(enabled, changeVisibility);
            if (!enabled)
            {
                _timer.Stop(); _publisher?.Dispose(); _publisher = null;
                TaskbarRegistration.Unregister();
            }
            SetStatus(!enabled ? "Taskbar display is off." : shown
                ? "Taskbar toolbar enabled. Hover for details; click to open AQI Clock."
                : "Enabled. Right-click the taskbar, then select Toolbars → AQI Clock. If it is missing, sign out of Windows and back in.");
            return true;
        }
        catch (Exception error) when (IsConfigurationError(error))
        {
            _timer.Stop(); _publisher?.Dispose(); _publisher = null;
            LogUnavailable(_logger, error);
            SetStatus("Could not configure the taskbar display. Check the app log and try again.");
            return false;
        }
    }

    private void OnTick(object? sender, EventArgs args)
    {
        try { Publish(); }
        catch (Exception error) when (IsConfigurationError(error))
        {
            _timer.Stop(); _publisher?.Dispose(); _publisher = null;
            LogUnavailable(_logger, error);
            SetStatus("Taskbar display stopped. Turn it off and on to retry.");
        }
    }

    private void Publish()
    {
        bool signedIn = _session.Current.UserId is not null || _audience.Current.Role == DeviceAudienceRole.StudentDevice;
        (string text, string tooltip) = Format(signedIn, _clock.HasCurrentLesson, _clock.CurrentLesson,
            _clock.Remaining, _clock.CurrentDetail, _clock.NextLesson);
        _publisher?.Publish(text, tooltip);
    }

    internal static (string Text, string Tooltip) Format(bool signedIn, bool current, string lesson, string remaining, string detail, string next) =>
        !signedIn ? ("AQI Clock | Sign in", "Click to sign in to AQI Clock.") : current
            ? ($"{lesson} | {remaining} left", $"{lesson} · {remaining} left · {detail} · {next}")
            : (lesson, next);

    private void SetStatus(string value) { Status = value; StatusChanged?.Invoke(this, EventArgs.Empty); }
    private static bool IsConfigurationError(Exception error) =>
        error is IOException or UnauthorizedAccessException or SecurityException or System.Runtime.InteropServices.COMException;
    [LoggerMessage(Level = LogLevel.Warning, Message = "Taskbar display unavailable")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _timer.Tick -= OnTick;
        _messenger.UnregisterAll(this);
        _publisher?.Dispose(); _publisher = null;
        GC.SuppressFinalize(this);
    }
}
