using AqiClock.App.ViewModels;
using AqiClock.App.Services;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows.Threading;

namespace AqiClock.Application.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public async Task BetaUpdatesAreOptInAndSurviveSavingOtherSettings()
    {
        var settings = new SettingsStub();
        using var viewModel = new SettingsViewModel(settings, new SessionStub(), new SyncStub(),
            new WindowStub(), new NotificationStub(), new UpdateStub());
        Assert.False(viewModel.IncludeBetaUpdates);
        viewModel.IncludeBetaUpdates = true;
        await viewModel.SaveCommand.ExecuteAsync(null);
        using var reopened = new SettingsViewModel(settings, new SessionStub(), new SyncStub(),
            new WindowStub(), new NotificationStub(), new UpdateStub());
        Assert.True(reopened.IncludeBetaUpdates);
        reopened.EndWarningMinutes = 7;
        await reopened.SaveCommand.ExecuteAsync(null);
        Assert.True(settings.Current.IncludeBetaUpdates);
        reopened.IncludeBetaUpdates = false;
        await reopened.SaveCommand.ExecuteAsync(null);
        Assert.False(settings.Current.IncludeBetaUpdates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TaskbarPreferenceIsSavedOnlyWhenConfigurationSucceeds(bool succeeds)
    {
        var settings = new SettingsStub();
        var taskbar = new TaskbarStub { Succeeds = succeeds };
        using var viewModel = new SettingsViewModel(settings, new SessionStub(), new SyncStub(),
            new WindowStub(), new NotificationStub(), new UpdateStub(), taskbar: taskbar);
        viewModel.TaskbarDisplayEnabled = true;
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal(succeeds, settings.Current.TaskbarDisplayEnabled);
        Assert.True(taskbar.Requested);
        Assert.Equal(succeeds ? "Enabled" : "Failed", viewModel.TaskbarDisplayStatus);
    }

    [Fact]
    public async Task WindowsUpgradeDoesNotBlockSavingUnrelatedPreferences()
    {
        var settings = new SettingsStub();
        await settings.SaveAsync(settings.Current with { TaskbarDisplayEnabled = true });
        var taskbar = new TaskbarStub { IsAvailable = false };
        using var viewModel = new SettingsViewModel(settings, new SessionStub(), new SyncStub(),
            new WindowStub(), new NotificationStub(), new UpdateStub(), taskbar: taskbar);
        viewModel.EndWarningMinutes = 8;
        await viewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal(8, settings.Current.EndWarningMinutes);
        Assert.True(settings.Current.TaskbarDisplayEnabled);
        Assert.False(taskbar.Requested);
    }

    private sealed class TaskbarStub : ITaskbarDisplayService
    {
        public bool IsAvailable { get; init; } = true;
        public bool Succeeds { get; init; } = true;
        public bool Requested { get; private set; }
        public string Status { get; private set; } = "Off";
        public event EventHandler? StatusChanged;
        public bool Configure(bool enabled)
        {
            Requested = enabled;
            Status = Succeeds ? "Enabled" : "Failed";
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return Succeeds;
        }
    }
    [Fact]
    public void StudentDeviceUsesStudentAccountCopy()
    {
        var session = new SessionStub(new SessionState(Guid.NewGuid(), null, null, true, false, IsAnonymous: true));
        var audience = new DeviceAudienceContext(new WeakReferenceMessenger());
        audience.SetStudent([], []);
        using var viewModel = new SettingsViewModel(
            new SettingsStub(), session, new SyncStub(), new WindowStub(),
            new NotificationStub(), new UpdateStub(), audience: audience);

        Assert.Equal("Student device", viewModel.Email);
        Assert.Equal("Enrolled account", viewModel.AccountCaption);
        Assert.Equal("Student", viewModel.Role);
        Assert.True(viewModel.HasRole);
    }

    [Fact]
    public void SignedOutAccountHasNoRoleBadge()
    {
        using var viewModel = new SettingsViewModel(
            new SettingsStub(), new SessionStub(), new SyncStub(), new WindowStub(),
            new NotificationStub(), new UpdateStub());

        Assert.Equal("Signed out", viewModel.Email);
        Assert.Equal(string.Empty, viewModel.Role);
        Assert.False(viewModel.HasRole);
    }

    [Fact]
    public async Task ConnectivityChangeFromWorkerThreadIsMarshalledAndDisposeUnregisters()
    {
        Dispatcher dispatcher = WpfDispatcherHost.Dispatcher;
        var messenger = new WeakReferenceMessenger();
        SettingsViewModel? viewModel = null;
        int notifications = 0;
        await dispatcher.InvokeAsync(() =>
        {
            viewModel = new SettingsViewModel(
                new SettingsStub(), new SessionStub(), new SyncStub(), new WindowStub(),
                new NotificationStub(), new UpdateStub(), messenger);
            viewModel.SyncNowCommand.CanExecuteChanged += (_, _) => notifications++;
        });

        await Task.Run(() => messenger.Send(new ConnectivityChanged(ConnectivityState.Online, DateTimeOffset.UtcNow)));
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, notifications);

        await dispatcher.InvokeAsync(viewModel!.Dispose);
        messenger.Send(new ConnectivityChanged(ConnectivityState.Offline, null));
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.Equal(1, notifications);
    }

    private static class WpfDispatcherHost
    {
        private static readonly TaskCompletionSource<Dispatcher> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static readonly Thread Thread = Start();
        public static Dispatcher Dispatcher => Ready.Task.GetAwaiter().GetResult();

        private static Thread Start()
        {
            var thread = new Thread(() =>
            {
                Ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        }
    }

    private sealed class SettingsStub : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public event EventHandler<SettingsChanged>? Changed { add { } remove { } }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) { Current = settings; return Task.CompletedTask; }
    }

    private sealed class SessionStub(SessionState? state = null) : ISessionService
    {
        public SessionState Current => state ?? SessionState.SignedOut;
        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignInAsync(string email, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SyncStub : ISyncService
    {
        public ConnectivityState State { get; set; } = ConnectivityState.Online;
        public DateTimeOffset? LastSyncedAt => null;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncTableAsync(CacheTable table, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void SignalTableChanged(CacheTable table) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class UpdateStub : IUpdateService
    {
        public UpdateState Current { get; } = new(UpdateStatus.UpToDate);
        public event EventHandler<UpdateState>? StateChanged { add { } remove { } }
        public void Start() { }
        public void RequestRestartToApply() { }
        public void PrepareUpdateOnExit() { }
        public void Dispose() { }
    }

    private sealed class NotificationStub : INotificationPresenter
    {
        public Task ShowLessonStartAsync(NotificationEvent notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowEndWarningAsync(NotificationEvent notification, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowAnnouncementAsync(Announcement announcement, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowTestAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class WindowStub : IWindowService
    {
        public void ShowMainWindow() { }
        public void ShowSignInWindow() { }
        public void ShowPasswordRecoveryWindow(PasswordRecoveryRequest request) { }
        public void ClosePasswordRecoveryWindow() { }
        public void ShowSettingsWindow() { }
        public void ShowAdminWindow() { }
        public void CloseAdminWindow(string? reason = null) { }
        public bool Confirm(string message, string title) => true;
        public void ShowAnnouncements() { }
        public void HideMainWindow() { }
        public void ActivateMainWindow() { }
        public void CloseSignInWindow() { }
        public void ShutdownApplication() { }
        public void ExitApplication() { }
    }
}
