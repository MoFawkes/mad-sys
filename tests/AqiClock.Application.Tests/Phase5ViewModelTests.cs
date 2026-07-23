using AqiClock.App.Services;
using AqiClock.App.ViewModels;
using AqiClock.App.Views;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Configuration;
using AqiClock.Application.Messages;
using AqiClock.Application.Sync;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Time;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AqiClock.Application.Tests;

public sealed class Phase5ViewModelTests
{
    [Fact]
    public void CompactLayoutIsFixedFramelessStripAndNormalLayoutRestoresChrome()
    {
        WindowLayout compact = WindowLayouts.For(DisplayMode.Compact);
        WindowLayout normal = WindowLayouts.For(DisplayMode.Normal);

        Assert.Equal(320, compact.Width); Assert.Equal(80, compact.Height); Assert.True(compact.IsFrameless);
        Assert.Equal(820, normal.Width); Assert.Equal(560, normal.Height); Assert.False(normal.IsFrameless);
    }

    [Fact]
    public void CompactPlacementNormalizationDoesNotChangeNormalPlacement()
    {
        WindowPlacement normal = new(10, 20, 720, 760);
        AppSettings original = new() { NormalPlacement = normal };

        AppSettings updated = WindowPlacements.Apply(original, DisplayMode.Compact, new WindowPlacement(30, 40, 320, 760, true));

        Assert.Equal(normal, updated.NormalPlacement);
        Assert.Equal(new WindowPlacement(30, 40, 320, 80), updated.CompactPlacement);
    }

    [Fact]
    public void ClosingSignInExitsOnlyWhileSignedOut()
    {
        Assert.True(WindowLifecycle.ShouldExitAfterSignInClose(SessionState.SignedOut));
        Assert.False(WindowLifecycle.ShouldExitAfterSignInClose(SessionState.SignedOut, returnToRoleChoice: true));
        Assert.False(WindowLifecycle.ShouldExitAfterSignInClose(new SessionState(Guid.NewGuid(), "teacher@example.test", UserRole.Teacher, true, false)));
    }

    [Fact]
    public void SecondLaunchTargetsTheVisibleSignedOutSurface()
    {
        Assert.Equal(ActivationTarget.SignIn, WindowLifecycle.TargetForActivation(SessionState.SignedOut, false));
        Assert.Equal(ActivationTarget.PasswordRecovery, WindowLifecycle.TargetForActivation(SessionState.SignedOut, true));
        Assert.Equal(ActivationTarget.Main, WindowLifecycle.TargetForActivation(SessionState.SignedOut, false, studentSessionActive: true));
        Assert.Equal(ActivationTarget.Main, WindowLifecycle.TargetForActivation(
            new SessionState(Guid.NewGuid(), "teacher@example.test", UserRole.Teacher, true, false), false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SignInIsDisabledForBlankEmailWithoutThrowing(string email)
    {
        var vm = CreateSignInViewModel(new SessionStub(), new SyncStub());
        vm.Email = email; vm.Password = "not-empty";

        Assert.False(vm.SignInCommand.CanExecute(null));
    }

    [Fact]
    public void SignInWindowIsResizableAndScrollableAtLargeTextScaling()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new SignInWindow(CreateSignInViewModel(new SessionStub(), new SyncStub()));
                WpfUiTestResources.Attach(window);
                Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);
                Assert.True(window.MinWidth > 0);
                Assert.True(window.MinHeight > 0);
                var scroller = Assert.IsType<ScrollViewer>(window.FindName("AuthScroller"));
                Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
                window.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Sign-in window render timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public async Task UnexpectedLocalPersistenceFailureIsShownInWindow()
    {
        var vm = CreateSignInViewModel(new SessionStub(new IOException("disk unavailable")), new SyncStub());
        vm.Email = "teacher@example.test"; vm.Password = "not-empty";

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Contains("local data", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAuthenticationSyncFailureIsNotReportedAsBadCredentials()
    {
        var vm = CreateSignInViewModel(new SessionStub(), new SyncStub(new InvalidOperationException("realtime unavailable")));
        vm.Email = "teacher@example.test"; vm.Password = "correct-password";

        await vm.SignInCommand.ExecuteAsync(null);

        Assert.Contains("initial timetable download", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("password", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DurationFormattingHandlesMinutesAndHours()
    {
        Assert.Equal("04:09", ClockViewModel.FormatDuration(new TimeSpan(0, 4, 9)));
        Assert.Equal("1:02:03", ClockViewModel.FormatDuration(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public async Task ClockTransitionsFromCurrentToNextAtBoundary()
    {
        DateTime now = new(2026, 7, 16, 9, 59, 59);
        Guid timetableId = Guid.NewGuid();
        Timetable timetable = new(timetableId, "Normal", false, [new(Guid.NewGuid(), "Maths", new(9, 0), new(10, 0), 0), new(Guid.NewGuid(), "English", new(10, 5), new(11, 0), 1)]);
        var messenger = new WeakReferenceMessenger();
        var vm = new ClockViewModel(new TimetableRepository(timetable), new WeekRepository(timetableId, now.DayOfWeek), new OverrideRepository(), messenger);
        await vm.LoadAsync();
        messenger.Send(new ClockTick(now));
        Assert.Equal("Maths", vm.CurrentLesson);
        Assert.Equal("09:59", vm.ShortTimeText);
        Assert.Equal("Maths · 1 min left", vm.CompactLessonDetail);
        Assert.True(vm.TodayPeriods.Single(period => period.Name == "Maths").IsCurrent);
        Assert.True(vm.TodayPeriods.Single(period => period.Name == "English").IsUpcoming);
        messenger.Send(new ClockTick(now.AddSeconds(1)));
        Assert.Equal("No lesson right now", vm.CurrentLesson);
        Assert.Equal("Next: English at 10:05", vm.NextLesson);
    }

    [Fact]
    public async Task AnnouncementLoadCountsUnreadAndOpenMarksRead()
    {
        Guid author = Guid.NewGuid(); Guid id = Guid.NewGuid();
        var store = new ReadStore();
        var vm = new AnnouncementsViewModel(new AnnouncementRepository(new Announcement(id, "Meeting", "At 3", DateTimeOffset.Now.AddMinutes(-2), author, null)), store, new ProfileRepository(new Profile(author, "Sam", UserRole.Teacher, true)), new FixedClock(DateTime.Now), new WeakReferenceMessenger());
        await vm.LoadAsync(false); Assert.Equal(1, vm.UnreadCount);
        await vm.LoadAsync(true); Assert.Equal(0, vm.UnreadCount); Assert.True(await store.IsReadAsync(id));
    }

    [Fact]
    public async Task AnnouncementLoadCarriesEMasjidLinkIntoReaderDisplay()
    {
        Guid author = Guid.NewGuid();
        const string link = "https://example.com/e-masjid";
        var announcement = new Announcement(
            Guid.NewGuid(),
            "Programme",
            "Details",
            DateTimeOffset.Now.AddMinutes(-2),
            author,
            null,
            EMasjidLink: link);
        var vm = new AnnouncementsViewModel(
            new AnnouncementRepository(announcement),
            new ReadStore(),
            new ProfileRepository(new Profile(author, "Sam", UserRole.Teacher, true)),
            new FixedClock(DateTime.Now),
            new WeakReferenceMessenger());

        await vm.LoadAsync(false);

        AnnouncementDisplay display = Assert.Single(vm.Items);
        Assert.Equal(link, display.EMasjidLink);
        Assert.True(display.HasEMasjidLink);
    }

    [Fact]
    public async Task SettingsRoundTripPersistsTypedValues()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AqiClockTests", Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new AqiClockOptions { DataDirectory = directory });
            using var writer = new SettingsService(options);
            await writer.SaveAsync(new AppSettings { Theme = AppTheme.Dark, EndWarningMinutes = 12, CompactOnLaunch = true });
            using var reader = new SettingsService(options); await reader.LoadAsync();
            Assert.Equal(AppTheme.Dark, reader.Current.Theme); Assert.Equal(12, reader.Current.EndWarningMinutes); Assert.True(reader.Current.CompactOnLaunch);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ConcurrentSettingsSavesAreSerializedAndLeaveValidJson()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AqiClockSettingsRace", Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options.Create(new AqiClockOptions { DataDirectory = directory });
            using var service = new SettingsService(options);

            await Task.WhenAll(Enumerable.Range(0, 25).Select(index =>
                service.SaveAsync(new AppSettings { EndWarningMinutes = index % 16, CompactOnLaunch = index % 2 == 0 })));

            using var reader = new SettingsService(options);
            await reader.LoadAsync();
            Assert.InRange(reader.Current.EndWarningMinutes, 0, 15);
            Assert.False(File.Exists(Path.Combine(directory, "settings.json.tmp")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ConnectivityStatusAndOfflineCommandFollowState()
    {
        var messenger = new WeakReferenceMessenger();
        var sync = new SyncStub(); var session = new SessionStub(); var settings = new SettingsStub();
        var clock = new ClockViewModel(new TimetableRepository(), new WeekRepository(), new OverrideRepository(), messenger);
        var announcements = new AnnouncementsViewModel(new AnnouncementRepository(), new ReadStore(), new ProfileRepository(), new FixedClock(DateTime.Now), messenger);
        var vm = new MainViewModel(clock, announcements, sync, session, settings, new WindowStub(), messenger);
        messenger.Send(new ConnectivityChanged(ConnectivityState.Offline, DateTimeOffset.UtcNow.AddHours(-2)));
        Assert.Contains("Offline", vm.SyncStatus, StringComparison.Ordinal); Assert.False(vm.SyncNowCommand.CanExecute(null));
        messenger.Send(new ConnectivityChanged(ConnectivityState.Online, DateTimeOffset.UtcNow));
        Assert.Equal("Synced · just now", vm.SyncStatus); Assert.True(vm.SyncNowCommand.CanExecute(null));
        messenger.Send(new ConnectivityChanged(ConnectivityState.Offline, DateTimeOffset.UtcNow.AddMinutes(-2)));
        Assert.Contains("2 min ago", vm.SyncStatus, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTime now) : IClock { public DateTime Now => now; public DateOnly LocalToday => DateOnly.FromDateTime(now); }
    private sealed class TimetableRepository(params Timetable[] rows) : ITimetableRepository { public Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Timetable>>(rows); }
    private sealed class WeekRepository : IWeekScheduleRepository { private readonly WeekSchedule _value; public WeekRepository() => _value = WeekSchedule.Empty; public WeekRepository(Guid id, DayOfWeek day) => _value = new(new Dictionary<DayOfWeek, Guid?> { [day] = id }); public Task<WeekSchedule> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_value); }
    private sealed class OverrideRepository : IDateOverrideRepository { public Task<IReadOnlyList<DateOverride>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DateOverride>>([]); }
    private sealed class AnnouncementRepository(params Announcement[] rows) : IAnnouncementRepository { public Task<IReadOnlyList<Announcement>> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Announcement>>(rows); }
    private sealed class ProfileRepository(params Profile[] rows) : IProfileRepository { public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Profile>>(rows); public Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(rows.FirstOrDefault(x => x.Id == id)); }
    private sealed class ReadStore : IAnnouncementReadStore { private readonly HashSet<Guid> _read = []; public Task<bool> IsReadAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_read.Contains(id)); public Task MarkReadAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default) { _read.Add(id); return Task.CompletedTask; } }
    private static SignInViewModel CreateSignInViewModel(ISessionService session, ISyncService sync) => new(session, sync, new GatewayStub(), new WindowStub(), NullLogger<SignInViewModel>.Instance);
    private sealed class SyncStub(Exception? startFailure = null) : ISyncService { public ConnectivityState State { get; private set; } = ConnectivityState.Offline; public DateTimeOffset? LastSyncedAt => null; public Task StartAsync(CancellationToken cancellationToken = default) => startFailure is null ? Task.CompletedTask : Task.FromException(startFailure); public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SyncAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SyncTableAsync(CacheTable table, CancellationToken cancellationToken = default) => Task.CompletedTask; public void SignalTableChanged(CacheTable table) { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    private sealed class SessionStub(Exception? signInFailure = null) : ISessionService { public SessionState Current => SessionState.SignedOut; public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SignInAsync(string email, string password, CancellationToken cancellationToken = default) => signInFailure is null ? Task.CompletedTask : Task.FromException(signInFailure); public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class GatewayStub : ISupabaseGateway
    {
        public Task CompletePasswordRecoveryAsync(string accessToken, string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class SettingsStub : ISettingsService { public AppSettings Current => new(); public event EventHandler<SettingsChanged>? Changed { add { } remove { } } public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask; }
    private sealed class WindowStub : IWindowService { public void ShowMainWindow() { } public void ShowSignInWindow() { } public void ShowPasswordRecoveryWindow(PasswordRecoveryRequest request) { } public void ClosePasswordRecoveryWindow() { } public void ShowSettingsWindow() { } public void ShowAdminWindow() { } public void CloseAdminWindow(string? reason = null) { } public bool Confirm(string message, string title) => true; public void ShowAnnouncements() { } public void HideMainWindow() { } public void ActivateMainWindow() { } public void CloseSignInWindow() { } public void ShutdownApplication() { } public void ExitApplication() { } }
}
