using AqiClock.Application.Abstractions;
using AqiClock.Application.Services;
using AqiClock.Application.Sync;
using AqiClock.Application.Configuration;
using AqiClock.Domain.Entities;
using AqiClock.Infrastructure.Supabase;
using AqiClock.Infrastructure.Time;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Options;
using Supabase.Realtime.Exceptions;
using AqiClock.App.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;

namespace AqiClock.Application.Tests;

public sealed class InfrastructureOrchestrationTests
{
    [Fact]
    public void InstituteClockResolvesIanaZoneAndFallsBackForUnknownZone()
    {
        var messenger = new WeakReferenceMessenger();
        var london = new InstituteClock(new OrganizationRepository("Europe/London"), messenger, NullLogger<InstituteClock>.Instance);
        var unknown = new InstituteClock(new OrganizationRepository("Not/AZone"), messenger, NullLogger<InstituteClock>.Instance);

        Assert.Equal("Europe/London", london.TimeZoneId);
        Assert.Equal(TimeZoneInfo.Local.Id, unknown.TimeZoneId);
        Assert.Equal(DateOnly.FromDateTime(london.Now), london.LocalToday);
    }

    [Fact]
    public async Task IdenticalPulledSnapshotDoesNotPublishAnotherDataChanged()
    {
        var gateway = new FakeGateway();
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        int messages = 0;
        messenger.Register<AqiClock.Application.Messages.DataChanged>(recipient, (_, _) => messages++);
        await using SyncService sync = CreateSyncService(gateway, messenger: messenger);

        await sync.SyncTableAsync(CacheTable.Periods);
        await sync.SyncTableAsync(CacheTable.Periods);

        Assert.Equal(1, messages);
        Assert.Equal(2, gateway.PullCounts[CacheTable.Periods]);
    }

    [Fact]
    public void AudienceMutationsPublishChangedState()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new AudienceRecipient();
        messenger.Register(recipient);
        var audience = new DeviceAudienceContext(messenger);

        audience.SetStudent([Guid.NewGuid()], [SessionHalfDay.Pm]);
        audience.SetTeacher(UserRole.Admin);
        audience.Clear();

        Assert.Equal(
            [DeviceAudienceRole.StudentDevice, DeviceAudienceRole.Admin, DeviceAudienceRole.Teacher],
            recipient.States.Select(state => state.Role));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 300)]
    [InlineData(20, 300)]
    public void BackoffIsExponentialAndCapped(int failures, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), BackoffPolicy.GetDelay(failures));

    [Fact]
    public async Task SessionRestoreKeepsCacheWhenRefreshRequiresSignIn()
    {
        var cache = new FakeCache();
        var store = new FakeSessionStore { Session = new StoredSession("old", "expired", DateTimeOffset.UtcNow.AddDays(-1)) };
        var gateway = new FakeGateway { RefreshException = new UnauthorizedAccessException() };
        var service = new SessionService(store, gateway, new FakeProfiles(), cache, new WeakReferenceMessenger());

        await service.RestoreAsync();

        Assert.True(service.Current.RequiresSignIn);
        Assert.Equal(0, cache.WipeCount);
    }

    [Fact]
    public void OversizedDialogPlacementIsClampedInsideWorkArea()
    {
        WindowPlacement actual = WindowPlacements.Clamp(new WindowPlacement(-500, -300, 1400, 900), 0, 0, 1092, 614, 900, 480);
        Assert.Equal(new WindowPlacement(0, 0, 1092, 614), actual);
    }

    [Fact]
    public void SavedAdminPlacementStaysOnSecondaryAndClampsToItsWorkArea()
    {
        WindowPlacement actual = WindowPlacements.Clamp(
            new WindowPlacement(2600, 10, 1120, 780),
            2560, 0, 1280, 720, 900, 480);

        Assert.Equal(new WindowPlacement(2600, 0, 1120, 720), actual);
    }

    [Fact]
    public void SavedAdminPlacementFitsScaledSmallScreenWorkArea()
    {
        WindowPlacement actual = WindowPlacements.Clamp(
            new WindowPlacement(0, 0, 1120, 780),
            0, 0, 911, 485, 900, 480);

        Assert.Equal(new WindowPlacement(0, 0, 911, 485), actual);
    }

    [Theory]
    [InlineData(1120, 780)]
    [InlineData(700, 780)]
    public void FirstRunDialogPlacementFitsMeasuredSmallScreen(double width, double height)
    {
        WindowPlacement actual = WindowPlacements.Clamp(
            new WindowPlacement(80, 0, width, height),
            0, 0, 1280, 720, 560, 420);

        Assert.True(actual.Height <= 720);
        Assert.InRange(actual.Left, 0, 1280 - actual.Width);
        Assert.InRange(actual.Top, 0, 720 - actual.Height);
    }

    [Fact]
    public void MissingDialogPlacementUsesCurrentBoundsAndClampsToFit()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window { WindowStartupLocation = WindowStartupLocation.CenterOwner };
                using var controller = new WindowPlacementController(
                    window,
                    new SettingsStub(),
                    settings => settings.AdminPlacement,
                    (settings, placement) => settings with { AdminPlacement = placement },
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowPlacementController>.Instance);

                controller.RestorePlacement(
                    new WindowPlacement(80, 0, 1120, 780),
                    new Rect(0, 0, 1280, 720));

                Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
                Assert.Equal(80, window.Left);
                Assert.Equal(0, window.Top);
                Assert.Equal(1120, window.Width);
                Assert.Equal(720, window.Height);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Window placement test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void OverflowingDialogPlacementFallsBackToCenterOwnerWithoutThrowing()
    {
        Exception? failure = null;
        var logger = new WarningLogger<WindowPlacementController>();
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window
                {
                    Width = 1120,
                    Height = 780,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };
                var settings = new SettingsStub(new AppSettings
                {
                    AdminPlacement = new WindowPlacement(1e18, 1e18, 1120, 780),
                });
                using var controller = new WindowPlacementController(
                    window,
                    settings,
                    value => value.AdminPlacement,
                    (value, placement) => value with { AdminPlacement = placement },
                    logger);

                controller.RestorePlacement();

                Assert.Equal(WindowStartupLocation.CenterOwner, window.WindowStartupLocation);
                Assert.True(double.IsNaN(window.Left));
                Assert.True(double.IsNaN(window.Top));
                Assert.Equal(1120, window.Width);
                Assert.Equal(780, window.Height);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Corrupt placement test timed out.");
        Assert.Null(failure);
        Assert.Equal(1, logger.WarningCount);
    }

    [Fact]
    public async Task SessionRestoreUsesCachedProfileWhenNetworkIsUnavailable()
    {
        Guid userId = Guid.NewGuid();
        var stored = new StoredSession(JwtFor(userId), "refresh", DateTimeOffset.UtcNow.AddMinutes(30));
        var store = new FakeSessionStore { Session = stored };
        var gateway = new FakeGateway { RefreshException = new HttpRequestException("offline") };
        using var service = new SessionService(store, gateway, new FakeProfiles(new Profile(userId, "Teacher", UserRole.Teacher, true)), new FakeCache(), new WeakReferenceMessenger());

        await service.RestoreAsync();

        Assert.Equal(userId, service.Current.UserId);
        Assert.False(service.Current.RequiresSignIn);
        Assert.Equal(stored, store.Session);
        Assert.True(gateway.RestoreCalled);
    }

    [Fact]
    public async Task SessionRestoreTreatsHttpClientTimeoutAsOfflineAndPreservesStoredSession()
    {
        Guid userId = Guid.NewGuid();
        var stored = new StoredSession(JwtFor(userId), "refresh", DateTimeOffset.UtcNow.AddMinutes(30));
        var store = new FakeSessionStore { Session = stored };
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout elapsing.", new TimeoutException());
        var gateway = new FakeGateway { RefreshException = timeout };
        using var service = new SessionService(store, gateway, new FakeProfiles(new Profile(userId, "Teacher", UserRole.Teacher, true)), new FakeCache(), new WeakReferenceMessenger());

        await service.RestoreAsync();

        Assert.Equal(userId, service.Current.UserId);
        Assert.False(service.Current.RequiresSignIn);
        Assert.Equal(stored, store.Session);
        Assert.True(gateway.RestoreCalled);
    }

    [Fact]
    public async Task SessionRestorePropagatesCallerCancellationAndPreservesStoredSession()
    {
        Guid userId = Guid.NewGuid();
        var stored = new StoredSession(JwtFor(userId), "refresh", DateTimeOffset.UtcNow.AddMinutes(30));
        var store = new FakeSessionStore { Session = stored };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var gateway = new FakeGateway { RefreshException = new OperationCanceledException(cancellation.Token) };
        using var service = new SessionService(store, gateway, new FakeProfiles(), new FakeCache(), new WeakReferenceMessenger());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RestoreAsync(cancellation.Token));

        Assert.Equal(stored, store.Session);
        Assert.False(gateway.RestoreCalled);
    }

    [Fact]
    public void StartupStepsContinueAfterSessionAndNotificationFailures()
    {
        var started = new List<string>();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        Assert.False(StartupStepRunner.TryRun("session restore", () => throw new TaskCanceledException(), logger));
        Assert.True(StartupStepRunner.TryRun("updater", () => started.Add("updater"), logger));
        Assert.True(StartupStepRunner.TryRun("tray", () => started.Add("tray"), logger));
        Assert.False(StartupStepRunner.TryRun("notification scheduler", () => throw new System.IO.IOException("offline"), logger));
        Assert.True(StartupStepRunner.TryRun("clock", () => started.Add("clock"), logger));
        Assert.True(StartupStepRunner.TryRun("main window", () => started.Add("window"), logger));

        Assert.Equal(["updater", "tray", "clock", "window"], started);
    }

    [Fact]
    public async Task RealGatewayOfflineRestoreStartsFromCachedTokenWithoutSdkRoundTrip()
    {
        Guid userId = Guid.NewGuid();
        var store = new FakeSessionStore { Session = new StoredSession(JwtFor(userId), "refresh", DateTimeOffset.UtcNow.AddMinutes(30)) };
        using var gateway = new SupabaseGateway(Options.Create(new SupabaseOptions { Url = "http://127.0.0.1:1", AnonKey = "sb_publishable_test" }), Microsoft.Extensions.Logging.Abstractions.NullLogger<SupabaseGateway>.Instance);
        using var service = new SessionService(store, gateway, new FakeProfiles(new Profile(userId, "Teacher", UserRole.Teacher, true)), new FakeCache(), new WeakReferenceMessenger());

        await service.RestoreAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(userId, service.Current.UserId);
        Assert.False(service.Current.RequiresSignIn);
    }

    [Fact]
    public async Task CorruptCachedAccessTokenRequiresSignInInsteadOfCrashingStartup()
    {
        var store = new FakeSessionStore { Session = new StoredSession("not-a-jwt", "refresh", DateTimeOffset.UtcNow.AddMinutes(30)) };
        var gateway = new FakeGateway { RefreshException = new HttpRequestException("offline") };
        using var service = new SessionService(store, gateway, new FakeProfiles(), new FakeCache(), new WeakReferenceMessenger());

        await service.RestoreAsync();

        Assert.True(service.Current.RequiresSignIn);
        Assert.Null(store.Session);
    }

    [Fact]
    public async Task EnsureFreshRefreshesAndPersistsOnlyInsideExpiryWindow()
    {
        Guid userId = Guid.NewGuid();
        var store = new FakeSessionStore { Session = new StoredSession(JwtFor(userId), "old-refresh", DateTimeOffset.UtcNow.AddMinutes(4)) };
        var gateway = new FakeGateway { RefreshedSession = new AuthenticatedSession(userId, "teacher@example.test", "rotated", "rotated-refresh", DateTimeOffset.UtcNow.AddHours(1)) };
        using var service = new SessionService(store, gateway, new FakeProfiles(new Profile(userId, "Teacher", UserRole.Teacher, true)), new FakeCache(), new WeakReferenceMessenger());

        await service.EnsureFreshAsync();
        await service.EnsureFreshAsync();

        Assert.Equal(1, gateway.RefreshCalls);
        Assert.Equal("rotated", store.Session?.AccessToken);
    }

    [Fact]
    public async Task AudiencePreferencesRoundTripAndClearThroughCacheMeta()
    {
        var cache = new FakeCache();
        Guid classId = Guid.NewGuid();
        var first = new DeviceAudienceContext(new WeakReferenceMessenger(), cache);
        await first.SetStudentAsync([classId], [SessionHalfDay.Am]);
        var restored = new DeviceAudienceContext(new WeakReferenceMessenger(), cache);

        await restored.RestoreAsync();
        Assert.Contains(classId, restored.Current.SelectedClassIds);
        Assert.Contains(SessionHalfDay.Am, restored.Current.OptedHalfDays);

        await restored.ClearAsync();
        var cleared = new DeviceAudienceContext(new WeakReferenceMessenger(), cache);
        await cleared.RestoreAsync();
        Assert.Equal(DeviceAudienceRole.Teacher, cleared.Current.Role);
    }

    [Fact]
    public async Task TeacherRestoreClearsStaleStudentAudiencePreferences()
    {
        Guid userId = Guid.NewGuid();
        var cache = new FakeCache();
        var audience = new DeviceAudienceContext(new WeakReferenceMessenger(), cache);
        await audience.SetStudentAsync([Guid.NewGuid()], [SessionHalfDay.Pm]);
        var gateway = new FakeGateway { RefreshedSession = new AuthenticatedSession(userId, "teacher@example.test", JwtFor(userId), "refresh-2", DateTimeOffset.UtcNow.AddHours(1)) };
        var store = new FakeSessionStore { Session = new StoredSession(JwtFor(userId), "refresh-1", DateTimeOffset.UtcNow.AddMinutes(5)) };
        using var service = new SessionService(store, gateway, new FakeProfiles(new Profile(userId, "Teacher", UserRole.Teacher, true)), cache, new WeakReferenceMessenger(), audience);

        await service.RestoreAsync();

        Assert.Equal(UserRole.Teacher, service.Current.Role);
        Assert.Equal(DeviceAudienceRole.Teacher, audience.Current.Role);
        Assert.Equal(string.Empty, cache.Meta["student_preferences"]);
    }

    [Fact]
    public async Task StudentSyncSkipsProfiles()
    {
        var gateway = new FakeGateway();
        var audience = new DeviceAudienceContext(new WeakReferenceMessenger());
        await audience.SetStudentAsync([Guid.NewGuid()], []);
        await using var service = new SyncService(gateway, new FakeCache(), new WeakReferenceMessenger(), new DebouncePolicy(TimeSpan.Zero), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance, audience: audience);

        await service.SyncAllAsync();

        Assert.DoesNotContain(CacheTable.Profiles, gateway.PullCounts.Keys);
        Assert.Contains(CacheTable.Announcements, gateway.PullCounts.Keys);
    }

    [Fact]
    public async Task SessionRestoreDoesNotElevateFromCachedAdminProfile()
    {
        Guid userId = Guid.NewGuid();
        var store = new FakeSessionStore { Session = new StoredSession("old", "refresh", null) };
        var gateway = new FakeGateway { RefreshedSession = new AuthenticatedSession(userId, "admin@example.test", "new", "new-refresh", DateTimeOffset.UtcNow.AddHours(1)) };
        var service = new SessionService(store, gateway, new FakeProfiles(new Profile(userId, "Admin", UserRole.Admin, true)), new FakeCache(), new WeakReferenceMessenger());

        await service.RestoreAsync();

        Assert.Equal(UserRole.Teacher, service.Current.Role);
        Assert.Equal("new", store.Session?.AccessToken);
    }

    [Fact]
    public async Task SignInDoesNotElevateFromCachedAdminProfile()
    {
        Guid userId = Guid.NewGuid();
        var gateway = new FakeGateway
        {
            RefreshedSession = new AuthenticatedSession(userId, "admin@example.test", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        var service = new SessionService(
            new FakeSessionStore(),
            gateway,
            new FakeProfiles(new Profile(userId, "Stale Admin", UserRole.Admin, true)),
            new FakeCache(),
            new WeakReferenceMessenger());

        await service.SignInAsync("admin@example.test", "password");

        Assert.Equal(UserRole.Teacher, service.Current.Role);
    }

    [Fact]
    public async Task FreshProfileDataElevatesGenuineAdminAfterSignIn()
    {
        Guid userId = Guid.NewGuid();
        var messenger = new WeakReferenceMessenger();
        var profiles = new MutableProfiles(new Profile(userId, "Admin", UserRole.Admin, true));
        var gateway = new FakeGateway
        {
            RefreshedSession = new AuthenticatedSession(userId, "admin@example.test", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        var service = new SessionService(new FakeSessionStore(), gateway, profiles, new FakeCache(), messenger);

        await service.SignInAsync("admin@example.test", "password");
        Assert.Equal(UserRole.Teacher, service.Current.Role);

        messenger.Send(new AqiClock.Application.Messages.DataChanged(CacheTable.Profiles));
        await WaitUntilAsync(
            () => service.Current.Role == UserRole.Admin,
            "the refreshed profile to promote the session to Admin",
            () => $"role={service.Current.Role}");

        Assert.Equal(UserRole.Admin, service.Current.Role);
    }

    [Fact]
    public async Task ProfilesAreConfirmedBeforeALaterInitialSyncFailureReturns()
    {
        Guid userId = Guid.NewGuid();
        var messenger = new WeakReferenceMessenger();
        var gateway = new FakeGateway
        {
            RefreshedSession = new AuthenticatedSession(userId, "inactive@example.test", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        // Organizations and profiles succeed; the next table reproduces the
        // post-profile initial-sync failure that exposed D1.
        gateway.PullFailuresByTable[CacheTable.Timetables] = new InvalidOperationException("later table failed");
        using var session = new SessionService(new FakeSessionStore(), gateway, new FakeProfiles(), new FakeCache(), messenger);
        await session.SignInAsync("inactive@example.test", "password");
        await using var sync = new SyncService(
            gateway,
            new FakeCache(),
            messenger,
            new DebouncePolicy(TimeSpan.Zero),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance,
            session: session);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.StartAsync());

        Assert.True(session.Current.RoleConfirmed);
        Assert.False(session.Current.IsActive);
    }

    [Fact]
    public async Task MissingOrganizationConfirmsInactiveProfileBeforeInitialSyncFailureReturns()
    {
        Guid userId = Guid.NewGuid();
        var messenger = new WeakReferenceMessenger();
        var gateway = new FakeGateway
        {
            RefreshedSession = new AuthenticatedSession(userId, "inactive@example.test", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
            OrganizationFailure = new InvalidOperationException("The signed-in profile or student-device enrolment is unavailable."),
        };
        using var session = new SessionService(new FakeSessionStore(), gateway, new FakeProfiles(), new FakeCache(), messenger);
        await session.SignInAsync("inactive@example.test", "password");
        await using var sync = new SyncService(
            gateway,
            new FakeCache(),
            messenger,
            new DebouncePolicy(TimeSpan.Zero),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance,
            session: session);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sync.StartAsync());

        Assert.True(session.Current.RoleConfirmed);
        Assert.False(session.Current.IsActive);
    }

    [Fact]
    public async Task SecondIdenticalProfilesPullDoesNotPublishAnotherSessionChanged()
    {
        Guid userId = Guid.NewGuid();
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        int messages = 0;
        messenger.Register<AqiClock.Application.Messages.SessionChanged>(recipient, (_, _) => messages++);
        var gateway = new FakeGateway
        {
            RefreshedSession = new AuthenticatedSession(userId, "teacher@example.test", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)),
        };
        using var session = new SessionService(
            new FakeSessionStore(),
            gateway,
            new FakeProfiles(new Profile(userId, "Teacher", UserRole.Teacher, true)),
            new FakeCache(),
            messenger);
        await session.SignInAsync("teacher@example.test", "password");
        await using var sync = new SyncService(
            gateway,
            new FakeCache(),
            messenger,
            new DebouncePolicy(TimeSpan.Zero),
            TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance,
            session: session);

        await sync.SyncTableAsync(CacheTable.Profiles);
        int messagesAfterFirstPull = messages;
        await sync.SyncTableAsync(CacheTable.Profiles);

        Assert.Equal(2, messagesAfterFirstPull);
        Assert.Equal(messagesAfterFirstPull, messages);
        Assert.Equal(2, gateway.PullCounts[CacheTable.Profiles]);
    }

    [Fact]
    public async Task SyncWipesCacheOnOrganizationChangeThenRepopulates()
    {
        var cache = new FakeCache();
        cache.Meta["org_id"] = Guid.NewGuid().ToString();
        var gateway = new FakeGateway { OrganizationId = Guid.NewGuid() };
        await using var service = new SyncService(gateway, cache, new WeakReferenceMessenger(), new DebouncePolicy(TimeSpan.Zero), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance);

        await service.SyncAllAsync();

        Assert.Equal(1, cache.WipeCount);
        Assert.Equal(Enum.GetValues<CacheTable>().Length, cache.Replaced.Count);
        Assert.Equal(ConnectivityState.Online, service.State);
    }

    [Fact]
    public async Task RealtimeSignalsAreDebouncedPerTable()
    {
        var cache = new FakeCache();
        var gateway = new FakeGateway();
        var time = new FakeTimeProvider();
        await using var service = new SyncService(gateway, cache, new WeakReferenceMessenger(), new DebouncePolicy(TimeSpan.FromMilliseconds(40)), time, Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance);
        service.SignalTableChanged(CacheTable.Timetables);
        service.SignalTableChanged(CacheTable.Timetables);
        service.SignalTableChanged(CacheTable.Timetables);

        time.Advance(TimeSpan.FromMilliseconds(40));
        await WaitUntilAsync(
            () => gateway.PullCounts.GetValueOrDefault(CacheTable.Timetables) == 1,
            "the debounced timetable signal to perform one pull",
            () => $"pulls={gateway.PullCounts.GetValueOrDefault(CacheTable.Timetables)}");

        Assert.Equal(1, gateway.PullCounts.GetValueOrDefault(CacheTable.Timetables));
    }

    [Fact]
    public async Task StartupSyncCompletesWhenRealtimeSubscriptionFails()
    {
        var gateway = new FakeGateway();
        gateway.SubscriptionFailures.Enqueue(new RealtimeException("key rotation still propagating"));
        await using var service = CreateSyncService(gateway);

        await service.StartAsync();

        Assert.Equal(ConnectivityState.Online, service.State);
        Assert.NotNull(service.LastSyncedAt);
        Assert.Equal(1, gateway.SubscribeCalls);
        Assert.Equal(Enum.GetValues<CacheTable>().Length, gateway.PullCounts.Values.Sum());
    }

    [Fact]
    public async Task HeartbeatRetriesRealtimeSubscriptionUntilItAttaches()
    {
        var gateway = new FakeGateway();
        var time = new FakeTimeProvider();
        gateway.SubscriptionFailures.Enqueue(new RealtimeException("temporary 403"));
        await using var service = CreateSyncService(gateway, TimeSpan.FromMilliseconds(50), timeProvider: time);

        await service.StartAsync();
        await WaitUntilAsync(
            () => gateway.SubscribeCalls == 1,
            "the initial realtime subscription attempt to fail",
            () => $"calls={gateway.SubscribeCalls}, active={gateway.ActiveSubscriptions}");
        time.Advance(TimeSpan.FromMilliseconds(50));
        await WaitUntilAsync(
            () => gateway.SubscribeCalls >= 2 && gateway.ActiveSubscriptions == 1 && service.State == ConnectivityState.Online,
            "the heartbeat to retry and attach realtime",
            () => $"calls={gateway.SubscribeCalls}, active={gateway.ActiveSubscriptions}, state={service.State}");

        Assert.Equal(1, gateway.ActiveSubscriptions);
        Assert.Equal(ConnectivityState.Online, service.State);
    }

    [Fact]
    public async Task HeartbeatSurvivesUnexpectedRefreshFailure()
    {
        var gateway = new FakeGateway();
        var time = new FakeTimeProvider();
        await using var service = CreateSyncService(gateway, TimeSpan.FromMilliseconds(15), timeProvider: time);
        await service.StartAsync();
        int successfulPulls = gateway.PullCounts.Values.Sum();
        gateway.PullFailures.Enqueue(new InvalidOperationException("unexpected PostgREST-style failure"));

        time.Advance(TimeSpan.FromMilliseconds(15));
        await WaitUntilAsync(
            () => service.State == ConnectivityState.Offline,
            "the failed heartbeat refresh to transition offline",
            () => $"state={service.State}, pulls={gateway.PullCounts.Values.Sum()}");
        time.Advance(TimeSpan.FromMilliseconds(30));
        int fullRefresh = Enum.GetValues<CacheTable>().Length;
        await WaitUntilAsync(
            () => service.State == ConnectivityState.Online && gateway.PullCounts.Values.Sum() >= successfulPulls + fullRefresh + 1,
            "the next heartbeat to recover after an unexpected pull failure",
            () => $"state={service.State}, pulls={gateway.PullCounts.Values.Sum()}, expected>={successfulPulls + fullRefresh + 1}");

        Assert.Equal(ConnectivityState.Online, service.State);
        Assert.True(gateway.PullCounts.Values.Sum() > successfulPulls);
    }

    [Fact]
    public async Task HeartbeatSurvivesTransientTokenRefreshFailure()
    {
        var gateway = new FakeGateway();
        var time = new FakeTimeProvider();
        var session = new RefreshingSession(new HttpRequestException("offline"));
        await using var service = new SyncService(gateway, new FakeCache(), new WeakReferenceMessenger(), new DebouncePolicy(TimeSpan.Zero), time, Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance, TimeSpan.FromMilliseconds(15), session);
        await service.StartAsync();
        int initialPulls = gateway.PullCounts.Values.Sum();

        time.Advance(TimeSpan.FromMilliseconds(15));
        await WaitUntilAsync(
            () => service.State == ConnectivityState.Offline && session.Calls == 1,
            "the transient token refresh failure to transition offline",
            () => $"state={service.State}, sessionCalls={session.Calls}");
        time.Advance(TimeSpan.FromMilliseconds(15));
        await WaitUntilAsync(
            () => session.Calls >= 2 && gateway.PullCounts.Values.Sum() > initialPulls && service.State == ConnectivityState.Online,
            "the next heartbeat to recover after a transient token refresh failure",
            () => $"state={service.State}, sessionCalls={session.Calls}, pulls={gateway.PullCounts.Values.Sum()}");

        Assert.Equal(ConnectivityState.Online, service.State);
    }

    [Fact]
    public async Task HeartbeatSurvivesThrowingConnectivityRecipient()
    {
        var gateway = new FakeGateway();
        var messenger = new WeakReferenceMessenger();
        var recipient = new ThrowingConnectivityRecipient();
        var logger = new CapturingLogger<SyncService>();
        var time = new FakeTimeProvider();
        messenger.Register(recipient);
        await using var service = CreateSyncService(gateway, TimeSpan.FromMilliseconds(50), messenger, logger, time);

        await service.StartAsync();
        int initialPulls = gateway.PullCounts.Values.Sum();
        time.Advance(TimeSpan.FromMilliseconds(50));
        await WaitUntilAsync(
            () => gateway.PullCounts.Values.Sum() > initialPulls && service.State == ConnectivityState.Online,
            "the heartbeat to survive a throwing connectivity recipient",
            () => $"state={service.State}, recipientCalls={recipient.Calls}, pulls={gateway.PullCounts.Values.Sum()}");

        Assert.Equal(ConnectivityState.Online, service.State);
        Assert.True(recipient.Calls >= 3);
        Assert.Contains(logger.Errors, exception => exception is InvalidOperationException);
    }

    [Fact]
    public async Task HeartbeatResubscribesAfterRealtimeSocketDrops()
    {
        var gateway = new FakeGateway();
        var time = new FakeTimeProvider();
        await using var service = CreateSyncService(gateway, TimeSpan.FromMilliseconds(15), timeProvider: time);

        await service.StartAsync();
        await WaitUntilAsync(
            () => gateway.LatestSubscription is not null && gateway.ActiveSubscriptions == 1,
            "the initial realtime subscription to attach",
            () => $"calls={gateway.SubscribeCalls}, active={gateway.ActiveSubscriptions}");
        FakeSubscription dropped = gateway.LatestSubscription!;
        dropped.Drop();

        time.Advance(TimeSpan.FromMilliseconds(15));
        await WaitUntilAsync(
            () => gateway.SubscribeCalls >= 2 && gateway.ActiveSubscriptions == 1 && gateway.LatestSubscription != dropped,
            "the heartbeat to replace the dropped realtime subscription",
            () => $"calls={gateway.SubscribeCalls}, active={gateway.ActiveSubscriptions}, replaced={gateway.LatestSubscription != dropped}");

        Assert.False(dropped.IsAlive);
        Assert.True(gateway.LatestSubscription!.IsAlive);
    }

    [Fact]
    public async Task StopThenStartRestartsHeartbeatAndRealtime()
    {
        var gateway = new FakeGateway();
        var time = new FakeTimeProvider();
        await using var service = CreateSyncService(gateway, TimeSpan.FromMilliseconds(15), timeProvider: time);

        await service.StartAsync();
        await WaitUntilAsync(
            () => gateway.ActiveSubscriptions == 1,
            "the initial realtime subscription to attach before stopping",
            () => $"calls={gateway.SubscribeCalls}, active={gateway.ActiveSubscriptions}");
        await service.StopAsync();
        int pullsAfterStop = gateway.PullCounts.Values.Sum();
        time.Advance(TimeSpan.FromMilliseconds(60));
        await Task.Yield();

        Assert.Equal(0, gateway.ActiveSubscriptions);
        Assert.Equal(pullsAfterStop, gateway.PullCounts.Values.Sum());
        Assert.Equal(ConnectivityState.Offline, service.State);

        await service.StartAsync();
        await WaitUntilAsync(
            () => gateway.SubscribeCalls >= 2 && service.State == ConnectivityState.Online,
            "restart to attach realtime and complete initial sync",
            () => $"calls={gateway.SubscribeCalls}, active={gateway.ActiveSubscriptions}, state={service.State}");

        Assert.Equal(1, gateway.ActiveSubscriptions);
        Assert.Equal(ConnectivityState.Online, service.State);
    }

    [Fact]
    public async Task SignedOutHeartbeatTickDoesNotLogError()
    {
        var gateway = new FakeGateway();
        var logger = new CapturingLogger<SyncService>();
        var time = new FakeTimeProvider();
        await using var service = CreateSyncService(gateway, TimeSpan.FromMilliseconds(15), logger: logger, timeProvider: time);
        await service.StartAsync();
        gateway.IsSignedOut = true;

        time.Advance(TimeSpan.FromMilliseconds(15));
        await WaitUntilAsync(
            () => service.State == ConnectivityState.Offline,
            "the signed-out heartbeat to transition offline",
            () => $"state={service.State}, errors={logger.Errors.Count}");
        time.Advance(TimeSpan.FromMilliseconds(50));
        await Task.Yield();

        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task SessionChangedSignedOutStopsSync()
    {
        var gateway = new FakeGateway();
        var messenger = new WeakReferenceMessenger();
        await using var service = CreateSyncService(gateway, TimeSpan.FromMilliseconds(15), messenger);
        await service.StartAsync();
        await WaitUntilAsync(
            () => gateway.ActiveSubscriptions == 1,
            "the realtime subscription to attach before sign-out",
            () => $"calls={gateway.SubscribeCalls}, active={gateway.ActiveSubscriptions}");

        messenger.Send(new AqiClock.Application.Messages.SessionChanged(SessionState.SignedOut));
        await WaitUntilAsync(
            () => gateway.ActiveSubscriptions == 0 && service.State == ConnectivityState.Offline,
            "sign-out to stop sync and dispose realtime",
            () => $"active={gateway.ActiveSubscriptions}, state={service.State}");

        Assert.Equal(0, gateway.ActiveSubscriptions);
    }

    [Fact]
    public void RealtimeUpgradeDoesNotSendPublishableKeyAsBearerHeader()
    {
        using var gateway = new SupabaseGateway(
            Options.Create(new SupabaseOptions
            {
                Url = "https://example.supabase.co",
                AnonKey = "sb_publishable_example",
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SupabaseGateway>.Instance);
        var clientField = typeof(SupabaseGateway).GetField(
            "_client",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var client = Assert.IsType<global::Supabase.Client>(clientField?.GetValue(gateway));

        IReadOnlyDictionary<string, string> headers =
            client.Realtime.GetHeaders?.Invoke() ?? new Dictionary<string, string>();

        Assert.DoesNotContain(headers.Keys, key =>
            string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headers.Values, value =>
            value.Contains("sb_publishable_", StringComparison.Ordinal));
    }

    private static SyncService CreateSyncService(
        FakeGateway gateway,
        TimeSpan? heartbeatInterval = null,
        IMessenger? messenger = null,
        ILogger<SyncService>? logger = null,
        TimeProvider? timeProvider = null) =>
        new(
            gateway,
            new FakeCache(),
            messenger ?? new WeakReferenceMessenger(),
            new DebouncePolicy(TimeSpan.Zero),
            timeProvider ?? TimeProvider.System,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance,
            heartbeatInterval);

    private static async Task WaitUntilAsync(Func<bool> condition, string because, Func<string> observed)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed >= TimeSpan.FromSeconds(10))
            {
                Assert.Fail($"Timed out waiting for {because}. Observed: {observed()}");
            }
            await Task.Delay(10);
        }
    }

    [Theory]
    [InlineData("refresh_token_not_found")]
    [InlineData("refresh_token_already_used")]
    [InlineData("invalid_grant")]
    [InlineData("validation_failed")]
    public void ModernGoTrueRefreshErrorCodesRequireReauthentication(string errorCode)
    {
        var method = typeof(SupabaseGateway).GetMethod("IsRejectedRefresh", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.True((bool)method!.Invoke(null, [System.Net.HttpStatusCode.BadRequest, errorCode])!);
    }

    [Fact]
    public async Task CachedAccessTokenRestoreDoesNotCreateSdkSessionOrContactServer()
    {
        using var gateway = new SupabaseGateway(Options.Create(new SupabaseOptions { Url = "https://127.0.0.1:1", AnonKey = "sb_publishable_test" }), Microsoft.Extensions.Logging.Abstractions.NullLogger<SupabaseGateway>.Instance);
        await gateway.RestoreAccessTokenAsync(new StoredSession("cached-access", "cached-refresh", DateTimeOffset.UtcNow.AddHours(1))).WaitAsync(TimeSpan.FromSeconds(1));
        var clientField = typeof(SupabaseGateway).GetField("_client", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var client = Assert.IsType<global::Supabase.Client>(clientField?.GetValue(gateway));

        Assert.Null(client.Auth.CurrentSession);
    }

    private static string JwtFor(Guid userId)
    {
        static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"none\"}")}.{Encode($"{{\"sub\":\"{userId}\"}}")}.signature";
    }

    private sealed class FakeSessionStore : ISessionStore
    {
        public StoredSession? Session { get; set; }
        public Task<StoredSession?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Session);
        public Task SaveAsync(StoredSession session, CancellationToken cancellationToken = default) { Session = session; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken cancellationToken = default) { Session = null; return Task.CompletedTask; }
    }

    private sealed class OrganizationRepository(string timeZone) : IOrganizationRepository
    {
        public Task<OrganizationInfo?> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationInfo?>(new(Guid.NewGuid(), "AQI", timeZone));
    }

    private sealed class FakeProfiles(Profile? profile = null) : IProfileRepository
    {
        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Profile>>(profile is null ? [] : [profile]);
        public Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(profile?.Id == id ? profile : null);
    }

    private sealed class FakeCache : ILocalCache
    {
        public Dictionary<string, string> Meta { get; } = [];
        public List<CacheSnapshot> Replaced { get; } = [];
        public int WipeCount { get; private set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WipeAsync(CancellationToken cancellationToken = default) { WipeCount++; Meta.Clear(); Replaced.Clear(); return Task.CompletedTask; }
        public Task ReplaceSnapshotAsync(CacheSnapshot snapshot, CancellationToken cancellationToken = default) { Replaced.Add(snapshot); return Task.CompletedTask; }
        public Task<string?> GetMetaAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Meta.GetValueOrDefault(key));
        public Task SetMetaAsync(string key, string value, CancellationToken cancellationToken = default) { Meta[key] = value; return Task.CompletedTask; }
        public Task<DateTimeOffset?> GetLastSyncedAtAsync(CacheTable table, CancellationToken cancellationToken = default) => Task.FromResult<DateTimeOffset?>(null);
    }

    private sealed class FakeGateway : ISupabaseGateway
    {
        public Task CompletePasswordRecoveryAsync(string accessToken, string newPassword, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Guid OrganizationId { get; init; } = Guid.NewGuid();
        public Exception? OrganizationFailure { get; init; }
        public Exception? RefreshException { get; init; }
        public AuthenticatedSession RefreshedSession { get; init; } = new(Guid.NewGuid(), "teacher@example.test", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Dictionary<CacheTable, int> PullCounts { get; } = [];
        public Queue<Exception> SubscriptionFailures { get; } = [];
        public Queue<Exception> PullFailures { get; } = [];
        public Dictionary<CacheTable, Exception> PullFailuresByTable { get; } = [];
        public int SubscribeCalls { get; private set; }
        public int ActiveSubscriptions { get; private set; }
        public bool IsSignedOut { get; set; }
        public bool RestoreCalled { get; private set; }
        public int RefreshCalls { get; private set; }
        public FakeSubscription? LatestSubscription { get; private set; }
        public Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default) => Task.FromResult(RefreshedSession);
        public Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default) { RefreshCalls++; return RefreshException is null ? Task.FromResult(RefreshedSession) : Task.FromException<AuthenticatedSession>(RefreshException); }
        public Task RestoreAccessTokenAsync(StoredSession session, CancellationToken cancellationToken = default) { RestoreCalled = true; return Task.CompletedTask; }
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default) =>
            OrganizationFailure is not null
                ? Task.FromException<Guid>(OrganizationFailure)
                : IsSignedOut
                ? Task.FromException<Guid>(new InvalidOperationException("A session is required."))
                : Task.FromResult(OrganizationId);
        public Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default)
        {
            PullCounts[table] = PullCounts.GetValueOrDefault(table) + 1;
            if (PullFailuresByTable.TryGetValue(table, out Exception? tableException))
                return Task.FromException<CacheSnapshot>(tableException);
            if (PullFailures.TryDequeue(out Exception? exception)) return Task.FromException<CacheSnapshot>(exception);
            return Task.FromResult(new CacheSnapshot(table, [], DateTimeOffset.UtcNow));
        }
        public Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveTimetableAsync(TimetableRow timetable, IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveWeekScheduleRowAsync(int weekday, Guid? audienceClassId, Guid? timetableId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteWeekScheduleRowAsync(int weekday, Guid audienceClassId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AuditEntry>>([]);
        public Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default)
        {
            SubscribeCalls++;
            if (SubscriptionFailures.TryDequeue(out Exception? exception)) return Task.FromException<IRealtimeSubscription>(exception);
            ActiveSubscriptions++;
            LatestSubscription = new FakeSubscription(() => ActiveSubscriptions--);
            return Task.FromResult<IRealtimeSubscription>(LatestSubscription);
        }
    }

    private sealed class FakeSubscription(Action? onDispose = null) : IRealtimeSubscription
    {
        private int _isAlive = 1;
        private int _disposed;
        public bool IsAlive => Volatile.Read(ref _isAlive) == 1;
        public event EventHandler? Closed;
        public void Drop()
        {
            if (Interlocked.Exchange(ref _isAlive, 0) == 1) Closed?.Invoke(this, EventArgs.Empty);
        }
        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _isAlive, 0);
            if (Interlocked.Exchange(ref _disposed, 1) == 0) onDispose?.Invoke();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RefreshingSession(Exception firstFailure) : ISessionService
    {
        public int Calls { get; private set; }
        public SessionState Current => new(Guid.NewGuid(), "teacher@example.test", UserRole.Teacher, true, false);
        public Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignInAsync(string email, string password, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task EnsureFreshAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Calls == 1 ? Task.FromException(firstFailure) : Task.CompletedTask;
        }
    }

    private sealed class MutableProfiles(Profile? profile = null) : IProfileRepository
    {
        public Profile? Value { get; set; } = profile;
        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Profile>>(Value is null ? [] : [Value]);
        public Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Value?.Id == id ? Value : null);
    }

    private sealed class SettingsStub(AppSettings? settings = null) : ISettingsService
    {
        public AppSettings Current { get; } = settings ?? new();
        public event EventHandler<SettingsChanged>? Changed { add { } remove { } }
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class WarningLogger<T> : ILogger<T>
    {
        public int WarningCount { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) WarningCount++;
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Exception> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null) Errors.Add(exception);
        }
    }

    public sealed class AudienceRecipient : IRecipient<AqiClock.Application.Messages.AudienceChanged>
    {
        public List<DeviceAudience> States { get; } = [];
        public void Receive(AqiClock.Application.Messages.AudienceChanged message) => States.Add(message.State);
    }

    public sealed class ThrowingConnectivityRecipient : IRecipient<AqiClock.Application.Messages.ConnectivityChanged>
    {
        public int Calls { get; private set; }
        public void Receive(AqiClock.Application.Messages.ConnectivityChanged message)
        {
            Calls++;
            throw new InvalidOperationException("UI recipient failed");
        }
    }
}
