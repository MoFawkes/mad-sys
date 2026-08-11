using System.Diagnostics;
using System.IO;
using AqiClock.Application.Abstractions;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AqiClock.App.Services;
using AqiClock.Application.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows.Threading;

namespace AqiClock.App.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable, IRecipient<ConnectivityChanged>
{
    private readonly ISettingsService _settings;
    private readonly ISessionService _session;
    private readonly ISyncService _sync;
    private readonly IWindowService _windows;
    private readonly INotificationPresenter _notifications;
    private readonly IUpdateService _updates;
    private readonly IMessenger? _messenger;
    private readonly INotificationScheduler? _scheduler;
    private readonly IDeviceAudienceContext? _audience;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _startMinimized;
    [ObservableProperty] private bool _closeToTray;
    [ObservableProperty] private AppTheme _theme;
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _compactOnLaunch;
    [ObservableProperty] private bool _lessonStartNotifications;
    [ObservableProperty] private bool _endWarningNotifications;
    [ObservableProperty] private int _endWarningMinutes;
    [ObservableProperty] private bool _announcementNotifications;
    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private string _notificationHealth = "Today: notification plan not loaded yet";
    private bool IsStudentDevice => _session.Current.IsAnonymous || _audience?.Current.Role == DeviceAudienceRole.StudentDevice;
    public string Email => IsStudentDevice ? "Student device" : _session.Current.Email ?? "Signed out";
    public string Role => IsStudentDevice ? "Student" : _session.Current.UserId is null ? string.Empty : _session.Current.Role == UserRole.Admin ? "Admin" : "Teacher";
    public bool HasRole => !string.IsNullOrEmpty(Role);
    public bool CanSync => _sync.State == Application.Sync.ConnectivityState.Online;
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public string Version { get { _ = _updates.Current; return AppVersion.Current; } }

    public SettingsViewModel(ISettingsService settings, ISessionService session, ISyncService sync, IWindowService windows, INotificationPresenter notifications, IUpdateService updates, IMessenger? messenger = null, INotificationScheduler? scheduler = null, IDeviceAudienceContext? audience = null)
    { _settings = settings; _session = session; _sync = sync; _windows = windows; _notifications = notifications; _updates = updates; _messenger = messenger; _scheduler = scheduler; _audience = audience; _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher; UpdateStatus = updates.Current.DisplayText; updates.StateChanged += OnUpdateStateChanged; messenger?.Register(this); if (scheduler is not null) { NotificationHealth = scheduler.HealthSummary; scheduler.HealthChanged += OnNotificationHealthChanged; } Copy(settings.Current); }

    public void Receive(ConnectivityChanged message)
    {
        if (_dispatcher.CheckAccess()) SyncNowCommand.NotifyCanExecuteChanged();
        else _ = _dispatcher.BeginInvoke(SyncNowCommand.NotifyCanExecuteChanged);
    }

    [RelayCommand]
    private Task SaveAsync(CancellationToken token) => _settings.SaveAsync(new AppSettings
    { StartWithWindows = StartWithWindows, StartMinimized = StartMinimized, CloseToTray = CloseToTray, Theme = Theme, AlwaysOnTop = AlwaysOnTop, CompactOnLaunch = CompactOnLaunch, LessonStartNotifications = LessonStartNotifications, EndWarningNotifications = EndWarningNotifications, EndWarningMinutes = EndWarningMinutes, AnnouncementNotifications = AnnouncementNotifications, NormalPlacement = _settings.Current.NormalPlacement, CompactPlacement = _settings.Current.CompactPlacement, AdminPlacement = _settings.Current.AdminPlacement, SettingsPlacement = _settings.Current.SettingsPlacement }, token);

    [RelayCommand(CanExecute = nameof(CanSync))] private Task SyncNowAsync(CancellationToken token) => _sync.SyncAllAsync(token);
    [RelayCommand] private Task SendTestNotificationAsync(CancellationToken token) => _notifications.ShowTestAsync(token);
    [RelayCommand] private async Task SignOutAsync(CancellationToken token) { await _session.SignOutAsync(token); _windows.ShowRoleChoiceWindow(); }
    [RelayCommand] private static void ViewLogs()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqiClock", "logs");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Copy(AppSettings value)
    { StartWithWindows = value.StartWithWindows; StartMinimized = value.StartMinimized; CloseToTray = value.CloseToTray; Theme = value.Theme; AlwaysOnTop = value.AlwaysOnTop; CompactOnLaunch = value.CompactOnLaunch; LessonStartNotifications = value.LessonStartNotifications; EndWarningNotifications = value.EndWarningNotifications; EndWarningMinutes = value.EndWarningMinutes; AnnouncementNotifications = value.AnnouncementNotifications; }

    private void OnUpdateStateChanged(object? sender, UpdateState state)
    {
        if (_dispatcher.CheckAccess()) UpdateStatus = state.DisplayText;
        else _ = _dispatcher.BeginInvoke(() => UpdateStatus = state.DisplayText);
    }

    private void OnNotificationHealthChanged(object? sender, EventArgs args)
    {
        if (_scheduler is null) return;
        if (_dispatcher.CheckAccess()) NotificationHealth = _scheduler.HealthSummary;
        else _ = _dispatcher.BeginInvoke(() => NotificationHealth = _scheduler.HealthSummary);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _updates.StateChanged -= OnUpdateStateChanged;
        if (_scheduler is not null) _scheduler.HealthChanged -= OnNotificationHealthChanged;
        _messenger?.UnregisterAll(this);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
