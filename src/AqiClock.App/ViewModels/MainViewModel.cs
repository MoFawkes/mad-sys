using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.App.ViewModels;

public partial class MainViewModel : ObservableObject, IRecipient<ConnectivityChanged>, IRecipient<SessionChanged>
{
    private readonly ISyncService _sync;
    private readonly ISessionService _session;
    private readonly ISettingsService _settings;
    private readonly IWindowService _windows;
    [ObservableProperty] private DisplayMode _displayMode;
    [ObservableProperty] private string _syncStatus = "Offline";
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private bool _isAdmin;
    [ObservableProperty] private bool _isAnnouncementsOpen;
    [ObservableProperty] private bool _alwaysOnTop;

    public ClockViewModel Clock { get; }
    public AnnouncementsViewModel Announcements { get; }
    public bool CanEdit => IsAdmin && IsOnline;

    public MainViewModel(ClockViewModel clock, AnnouncementsViewModel announcements, ISyncService sync, ISessionService session, ISettingsService settings, IWindowService windows, IMessenger messenger)
    {
        Clock = clock; Announcements = announcements; _sync = sync; _session = session; _settings = settings; _windows = windows;
        DisplayMode = settings.Current.CompactOnLaunch ? DisplayMode.Compact : DisplayMode.Normal;
        AlwaysOnTop = settings.Current.AlwaysOnTop;
        ApplySession(session.Current); ApplyConnectivity(sync.State, sync.LastSyncedAt);
        messenger.Register<ConnectivityChanged>(this); messenger.Register<SessionChanged>(this);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    { await Clock.LoadAsync(cancellationToken); await Announcements.LoadAsync(false, cancellationToken); }

    [RelayCommand] private void ToggleDisplayMode() => DisplayMode = DisplayMode == DisplayMode.Normal ? DisplayMode.Compact : DisplayMode.Normal;
    [RelayCommand] private async Task TogglePinAsync()
    {
        AlwaysOnTop = !AlwaysOnTop;
        await _settings.SaveAsync(_settings.Current with { AlwaysOnTop = AlwaysOnTop });
    }
    [RelayCommand] private async Task ToggleAnnouncementsAsync() { IsAnnouncementsOpen = !IsAnnouncementsOpen; if (IsAnnouncementsOpen) await Announcements.LoadAsync(true); }
    [RelayCommand] private void OpenSettings() => _windows.ShowSettingsWindow();
    [RelayCommand(CanExecute = nameof(IsOnline))] private Task SyncNowAsync(CancellationToken token) => _sync.SyncAllAsync(token);
    [RelayCommand(CanExecute = nameof(CanEdit))] private void EditTimetables() => _windows.ShowAdminWindow();
    [RelayCommand(CanExecute = nameof(CanEdit))] private void ManageAnnouncements() => _windows.ShowAdminWindow();

    public void Receive(ConnectivityChanged message) => RunOnUiThread(() =>
    {
        ApplyConnectivity(message.State, message.LastSyncedAt);
        SyncNowCommand.NotifyCanExecuteChanged();
        EditTimetablesCommand.NotifyCanExecuteChanged();
        ManageAnnouncementsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEdit));
    });

    public void Receive(SessionChanged message) => RunOnUiThread(() =>
    {
        ApplySession(message.State);
        EditTimetablesCommand.NotifyCanExecuteChanged();
        ManageAnnouncementsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEdit));
        if (!IsAdmin) _windows.CloseAdminWindow();
    });

    private static void RunOnUiThread(Action action)
    {
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else _ = dispatcher.BeginInvoke(action);
    }

    private void ApplySession(SessionState state) { IsAdmin = state.Role == UserRole.Admin; if (state.RequiresSignIn) SyncStatus = "Sign in again to sync"; }
    private void ApplyConnectivity(ConnectivityState state, DateTimeOffset? synced)
    {
        IsOnline = state == ConnectivityState.Online;
        SyncStatus = state switch { ConnectivityState.Syncing => "Syncing…", ConnectivityState.Online => "Synced · just now", _ when _session.Current.RequiresSignIn => "Sign in again to sync", _ => synced is null ? "Offline" : $"Offline — last synced {Relative(synced.Value)}" };
    }
    private static string Relative(DateTimeOffset value) { TimeSpan age = DateTimeOffset.UtcNow - value; return age.TotalDays >= 7 ? "Timetable may be out of date" : age.TotalDays >= 1 ? $"{(int)age.TotalDays} d ago" : age.TotalHours >= 1 ? $"{(int)age.TotalHours} h ago" : "just now"; }
}
