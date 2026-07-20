using System.Diagnostics;
using System.IO;
using AqiClock.Application.Abstractions;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AqiClock.App.Services;

namespace AqiClock.App.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly ISessionService _session;
    private readonly ISyncService _sync;
    private readonly IWindowService _windows;
    private readonly INotificationPresenter _notifications;
    private readonly IUpdateService _updates;
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
    public string Email => _session.Current.Email ?? "Signed out";
    public string Role => _session.Current.Role == UserRole.Admin ? "Admin" : "Teacher";
    public bool CanSync => _sync.State == Application.Sync.ConnectivityState.Online;
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public string Version { get { _ = _updates.Current; return AppVersion.Current; } }

    public SettingsViewModel(ISettingsService settings, ISessionService session, ISyncService sync, IWindowService windows, INotificationPresenter notifications, IUpdateService updates)
    { _settings = settings; _session = session; _sync = sync; _windows = windows; _notifications = notifications; _updates = updates; UpdateStatus = updates.Current.DisplayText; updates.StateChanged += OnUpdateStateChanged; Copy(settings.Current); }

    [RelayCommand]
    private Task SaveAsync(CancellationToken token) => _settings.SaveAsync(new AppSettings
    { StartWithWindows = StartWithWindows, StartMinimized = StartMinimized, CloseToTray = CloseToTray, Theme = Theme, AlwaysOnTop = AlwaysOnTop, CompactOnLaunch = CompactOnLaunch, LessonStartNotifications = LessonStartNotifications, EndWarningNotifications = EndWarningNotifications, EndWarningMinutes = EndWarningMinutes, AnnouncementNotifications = AnnouncementNotifications, NormalPlacement = _settings.Current.NormalPlacement, CompactPlacement = _settings.Current.CompactPlacement }, token);

    [RelayCommand(CanExecute = nameof(CanSync))] private Task SyncNowAsync(CancellationToken token) => _sync.SyncAllAsync(token);
    [RelayCommand] private Task SendTestNotificationAsync(CancellationToken token) => _notifications.ShowTestAsync(token);
    [RelayCommand] private async Task SignOutAsync(CancellationToken token) { await _session.SignOutAsync(token); _windows.ShowSignInWindow(); }
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
        if (System.Windows.Application.Current.Dispatcher.CheckAccess()) UpdateStatus = state.DisplayText;
        else _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() => UpdateStatus = state.DisplayText);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _updates.StateChanged -= OnUpdateStateChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
