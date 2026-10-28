using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Application.Services;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using AqiClock.Domain.Time;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqiClock.Application.Tests;

public sealed class NotificationSchedulerTests
{
    private static readonly DateTime Monday = new(2026, 7, 20, 8, 30, 0, DateTimeKind.Local);

    [Theory]
    [InlineData(8, true, false, true, 1)]
    [InlineData(13, false, true, true, 1)]
    [InlineData(12, true, false, true, 0)]
    [InlineData(12, false, true, true, 1)]
    [InlineData(8, false, true, true, 0)]
    [InlineData(13, true, false, true, 0)]
    [InlineData(8, false, false, true, 0)]
    [InlineData(8, true, true, false, 0)]
    public async Task SharedNaseehahUsesHalfDayAndAnnouncementSettingAcrossClasses(
        int hour, bool am, bool pm, bool announcements, int expected)
    {
        DateTime now = Monday.Date.AddHours(hour);
        SchedulerFixture fixture = CreateFixture(now);
        Guid mine = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        Guid publicTimetable = Guid.NewGuid();
        fixture.Audience.SetStudent([mine],
            (am ? new[] { SessionHalfDay.Am } : []).Concat(pm ? [SessionHalfDay.Pm] : []));
        fixture.Settings.Value = fixture.Settings.Value with
        { LessonStartNotifications = false, AnnouncementNotifications = announcements };
        fixture.Timetables.Items = [new Timetable(publicTimetable, "Other track", false,
            [new Period(Guid.NewGuid(), "Isha + NASEEHAH", TimeOnly.FromDateTime(now), TimeOnly.FromDateTime(now.AddMinutes(25)), 0, false)])];
        fixture.Week = new FakeWeek(new WeekSchedule([
            new(Guid.NewGuid(), DayOfWeek.Monday, mine, null),
            new(Guid.NewGuid(), DayOfWeek.Monday, other, publicTimetable)]));
        using NotificationScheduler scheduler = fixture.Create();

        await scheduler.StartAsync();
        await scheduler.ProcessAsync(now);
        await scheduler.ProcessAsync(now.AddMinutes(20));

        Assert.Equal(expected, fixture.Presenter.Starts);
        Assert.Equal(0, fixture.Presenter.EndWarnings);
        if (expected > 0)
            Assert.Equal(NotificationEventKind.NaseehahStart, Assert.Single(Assert.Single(fixture.Presenter.StartGroups)).Kind);
    }

    [Fact]
    public async Task SharedNaseehahReplacesClassStartAndDeduplicatesAcrossRestart()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.Audience.SetStudent([Guid.NewGuid()], [SessionHalfDay.Am]);
        fixture.SetPeriods(
            new Period(Guid.NewGuid(), "Break / Naseehah", new(8, 30), new(9, 0), 0, false),
            new Period(Guid.NewGuid(), "Naseehah & Break", new(8, 30), new(9, 0), 1, false));
        using (NotificationScheduler first = fixture.Create())
        { await first.StartAsync(); await first.ProcessAsync(Monday); }
        using (NotificationScheduler second = fixture.Create())
        { await second.StartAsync(); await second.ProcessAsync(Monday); }

        Assert.Equal(1, fixture.Presenter.Starts);
        Assert.Single(fixture.Log.Entries);
        Assert.Equal("naseehah:2026-07-20:0830", fixture.Log.Entries.Keys.Single());
    }

    [Fact]
    public async Task AnnouncementSettingOffPreservesNaseehahClassAlerts()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.Audience.SetStudent([Guid.NewGuid()], [SessionHalfDay.Am]);
        fixture.Settings.Value = fixture.Settings.Value with { AnnouncementNotifications = false };
        fixture.SetPeriods(new Period(Guid.NewGuid(), "Naseehah", new(8, 30), new(9, 0), 0, false));
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync(); await scheduler.ProcessAsync(Monday);
        Assert.Equal(NotificationEventKind.LessonStart, Assert.Single(Assert.Single(fixture.Presenter.StartGroups)).Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangingAnnouncementSettingAtBoundaryDoesNotRepeatNaseehah(bool sharedFirst)
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.Audience.SetStudent([Guid.NewGuid()], [SessionHalfDay.Am]);
        fixture.Settings.Value = fixture.Settings.Value with { AnnouncementNotifications = sharedFirst };
        fixture.SetPeriods(new Period(Guid.NewGuid(), "Naseehah", new(8, 30), new(9, 0), 0, false));
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync(); await scheduler.ProcessAsync(Monday);
        fixture.Settings.Value = fixture.Settings.Value with { AnnouncementNotifications = !sharedFirst };
        await scheduler.RebuildAsync(Monday.AddSeconds(10));
        await scheduler.ProcessAsync(Monday.AddSeconds(10));
        Assert.Equal(1, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task SyncedClosureRemovesSharedNaseehahReminder()
    {
        SchedulerFixture fixture = CreateFixture(Monday.AddMinutes(-1));
        fixture.Audience.SetStudent([Guid.NewGuid()], [SessionHalfDay.Am]);
        fixture.SetPeriods(new Period(Guid.NewGuid(), "Naseehah", new(8, 30), new(9, 0), 0, false));
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync();
        fixture.Overrides.Items = [new DateOverride(Guid.NewGuid(), DateOnly.FromDateTime(Monday), null, "Closed")];
        await scheduler.RebuildAsync(Monday.AddSeconds(-1));
        await scheduler.ProcessAsync(Monday);
        Assert.Equal(0, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task SharedNaseehahResumesOnFollowingDayAndUsesRecentStartSuppression()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.Audience.SetStudent([Guid.NewGuid()], [SessionHalfDay.Am]);
        fixture.Settings.Value = fixture.Settings.Value with { EndWarningMinutes = 4 };
        fixture.SetPeriods(new Period(Guid.NewGuid(), "Naseehah", new(8, 30), new(8, 35), 0, false));
        Guid timetable = fixture.Timetables.Items.Single().Id;
        fixture.Week = new FakeWeek(new WeekSchedule(new Dictionary<DayOfWeek, Guid?>
        { [DayOfWeek.Monday] = timetable, [DayOfWeek.Tuesday] = timetable }));
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync(); await scheduler.ProcessAsync(Monday.AddMinutes(1));
        Assert.Equal(1, fixture.Presenter.Starts);
        Assert.Equal(0, fixture.Presenter.EndWarnings);
        await scheduler.ProcessAsync(Monday.AddDays(1));
        Assert.Equal(2, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task HolidayWeekIsSilentAcrossMidnightAndFollowingMondayResumes()
    {
        DateTime holidayMonday = new(2026, 10, 19, 8, 30, 0, DateTimeKind.Local);
        SchedulerFixture fixture = CreateFixture(holidayMonday.AddMinutes(-1));
        fixture.SetPeriod(holidayMonday.TimeOfDay, TimeSpan.FromMinutes(45));
        Guid timetableId = fixture.Timetables.Items.Single().Id;
        fixture.Week = new FakeWeek(new WeekSchedule(Enum.GetValues<DayOfWeek>()
            .ToDictionary(day => day, _ => (Guid?)timetableId)));
        fixture.Overrides.Items = Enumerable.Range(0, 7)
            .Select(offset => new DateOverride(Guid.NewGuid(), DateOnly.FromDateTime(holidayMonday.AddDays(offset)), null, "Holiday"))
            .ToArray();
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync();

        for (int day = 0; day < 7; day++)
        {
            await scheduler.ProcessAsync(holidayMonday.AddDays(day));
            await scheduler.ProcessAsync(holidayMonday.AddDays(day).AddMinutes(40));
        }

        Assert.Equal(0, fixture.Presenter.Starts);
        Assert.Equal(0, fixture.Presenter.EndWarnings);
        await scheduler.ProcessAsync(holidayMonday.AddDays(7));
        Assert.Equal(1, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task SyncedClosureRemovesAlreadyPlannedLessonAlerts()
    {
        SchedulerFixture fixture = CreateFixture(Monday.AddMinutes(-1));
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync();
        fixture.Overrides.Items = [new DateOverride(Guid.NewGuid(), DateOnly.FromDateTime(Monday), null, "Holiday")];
        var rebuilt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.HealthChanged += (_, _) => rebuilt.TrySetResult();

        fixture.Messenger.Send(new DataChanged(CacheTable.DateOverrides));
        await rebuilt.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await scheduler.ProcessAsync(Monday);
        await scheduler.ProcessAsync(Monday.AddMinutes(40));

        Assert.Equal(0, fixture.Presenter.Starts);
        Assert.Equal(0, fixture.Presenter.EndWarnings);
    }

    [Theory]
    [InlineData(120, 1, false)]
    [InlineData(121, 0, true)]
    public async Task GraceBoundaryFiresAt120SecondsAndSkipsAt121(int secondsLate, int expectedToasts, bool skipped)
    {
        SchedulerFixture fixture = CreateFixture(Monday.AddSeconds(secondsLate));
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        using NotificationScheduler scheduler = fixture.Create();

        await scheduler.StartAsync();
        await scheduler.ProcessAsync(fixture.Clock.Now);

        Assert.Equal(expectedToasts, fixture.Presenter.Starts);
        Assert.Equal(skipped, fixture.Log.Entries.Values.Single().Skipped);
    }

    [Fact]
    public async Task RecordedEventIsDeduplicatedAcrossRestart()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        using (NotificationScheduler first = fixture.Create()) { await first.StartAsync(); await first.ProcessAsync(Monday); }
        using (NotificationScheduler second = fixture.Create()) { await second.StartAsync(); await second.ProcessAsync(Monday); }
        Assert.Equal(1, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task SimultaneousLessonStartsArePresentedOnceAndLoggedIndividually()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.SetPeriods(
            new Period(Guid.NewGuid(), "Combined A", TimeOnly.FromDateTime(Monday), TimeOnly.FromDateTime(Monday.AddMinutes(30)), 0),
            new Period(Guid.NewGuid(), "Combined B", TimeOnly.FromDateTime(Monday), TimeOnly.FromDateTime(Monday.AddMinutes(45)), 1));
        using NotificationScheduler scheduler = fixture.Create();

        await scheduler.StartAsync();
        await scheduler.ProcessAsync(Monday);

        IReadOnlyList<NotificationEvent> group = Assert.Single(fixture.Presenter.StartGroups);
        Assert.Equal(["Combined A", "Combined B"], group.Select(item => item.Occurrence.Period.Name).Order());
        Assert.Equal(2, fixture.Log.Entries.Keys.Count(key => key.StartsWith("start:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SimultaneousEndWarningsArePresentedOnceAndLoggedIndividually()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.Settings.Value = fixture.Settings.Value with { EndWarningMinutes = 5 };
        fixture.SetPeriods(
            new Period(Guid.NewGuid(), "Combined A", new(8, 0), new(8, 35), 0),
            new Period(Guid.NewGuid(), "Combined B", new(8, 10), new(8, 35), 1));
        using NotificationScheduler scheduler = fixture.Create();

        await scheduler.StartAsync();
        await scheduler.ProcessAsync(Monday);

        IReadOnlyList<NotificationEvent> group = Assert.Single(fixture.Presenter.EndWarningGroups);
        Assert.Equal(["Combined A", "Combined B"], group.Select(item => item.Occurrence.Period.Name).Order());
        Assert.Equal(2, fixture.Log.Entries.Keys.Count(key => key.StartsWith("end-warning:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MovedFutureLessonCanFireAgainWithSameStableKey()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync(); await scheduler.ProcessAsync(Monday);

        fixture.SetPeriod(new TimeSpan(9, 0, 0), TimeSpan.FromMinutes(45));
        await scheduler.RebuildAsync(Monday.AddMinutes(1));
        await scheduler.ProcessAsync(Monday.AddMinutes(30));

        Assert.Equal(2, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task SameDayMovedBoundaryFiresAfterRestart()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        using (NotificationScheduler first = fixture.Create())
        {
            await first.StartAsync();
            await first.ProcessAsync(Monday);
        }

        fixture.SetPeriod(new TimeSpan(9, 0, 0), TimeSpan.FromMinutes(45));
        fixture.Clock.Now = Monday.AddMinutes(1);
        using (NotificationScheduler second = fixture.Create())
        {
            await second.StartAsync();
            await second.ProcessAsync(Monday.AddMinutes(30));
        }

        Assert.Equal(2, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task RemovedBoundaryDoesNotFire()
    {
        SchedulerFixture fixture = CreateFixture(Monday.AddMinutes(-1));
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync();
        fixture.Timetables.Items = [];
        await scheduler.RebuildAsync(Monday.AddSeconds(-30));
        await scheduler.ProcessAsync(Monday);
        Assert.Equal(0, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task RecentStartSuppressesEndWarning()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.Settings.Value = fixture.Settings.Value with { EndWarningMinutes = 4 };
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(5));
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync(); await scheduler.ProcessAsync(Monday.AddMinutes(1));
        Assert.Equal(1, fixture.Presenter.Starts);
        Assert.Equal(0, fixture.Presenter.EndWarnings);
        Assert.Contains(fixture.Log.Entries.Values, entry => entry.EventKey.StartsWith("end-warning:", StringComparison.Ordinal) && entry.Skipped);
    }

    [Fact]
    public async Task AnnouncementFiresOnlyOnFirstUnexpiredSighting()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.Announcements.Items = [new Announcement(Guid.NewGuid(), "Notice", "Body", new DateTimeOffset(Monday), Guid.NewGuid(), new DateTimeOffset(Monday.AddHours(1)))];
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync(); await scheduler.StartAsync();
        Assert.Equal(1, fixture.Presenter.Announcements);
    }

    [Fact]
    public async Task ExpiredAnnouncementAndDisabledCategoryStaySilent()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.Settings.Value = fixture.Settings.Value with { LessonStartNotifications = false };
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        fixture.Announcements.Items = [new Announcement(Guid.NewGuid(), "Old", "Body", new DateTimeOffset(Monday.AddDays(-1)), Guid.NewGuid(), new DateTimeOffset(Monday.AddSeconds(-1)))];
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync(); await scheduler.ProcessAsync(Monday);
        Assert.Equal(0, fixture.Presenter.Starts);
        Assert.Equal(0, fixture.Presenter.Announcements);
        Assert.Contains(fixture.Log.Entries.Values, entry => entry.Skipped);
    }

    [Theory]
    [InlineData(8, true, true, 1)]
    [InlineData(13, true, true, 1)]
    [InlineData(13, true, false, 1)]
    [InlineData(8, false, false, 0)]
    public async Task StudentPeriodNotificationsRequireOnlySelectedClassOverlap(
        int hour,
        bool selectedClassTagged,
        bool optIntoAmNaseehah,
        int expectedToasts)
    {
        DateTime now = new(2026, 7, 20, hour, 30, 0, DateTimeKind.Local);
        SchedulerFixture fixture = CreateFixture(now);
        fixture.SetPeriod(now.TimeOfDay, TimeSpan.FromMinutes(45));
        Guid selectedClass = Guid.NewGuid();
        fixture.Audience.SetStudent(
            [selectedClass],
            optIntoAmNaseehah ? [SessionHalfDay.Am] : []);
        fixture.Classes.ClassIds = selectedClassTagged ? new HashSet<Guid> { selectedClass } : new HashSet<Guid> { Guid.NewGuid() };
        using NotificationScheduler scheduler = fixture.Create();

        await scheduler.StartAsync();
        await scheduler.ProcessAsync(now);

        Assert.Equal(expectedToasts, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task UntaggedPeriodIsDeliveredToStudentSession()
    {
        SchedulerFixture fixture = CreateFixture(Monday);
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        fixture.Audience.SetStudent([Guid.NewGuid()], []);
        fixture.Classes.ClassIds = new HashSet<Guid>();
        using NotificationScheduler scheduler = fixture.Create();

        await scheduler.StartAsync();
        await scheduler.ProcessAsync(Monday);

        Assert.Equal(1, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task AudienceChangeToStudentRebuildsPlanAndDeliversClassBoundary()
    {
        DateTime beforeBoundary = Monday.AddMinutes(-1);
        SchedulerFixture fixture = CreateFixture(beforeBoundary);
        fixture.SetPeriod(Monday.TimeOfDay, TimeSpan.FromMinutes(45));
        Guid selectedClass = Guid.NewGuid();
        fixture.Classes.ClassIds = new HashSet<Guid> { selectedClass };
        using NotificationScheduler scheduler = fixture.Create();
        await scheduler.StartAsync();

        fixture.Audience.SetStudent([selectedClass], []);
        await WaitUntilAsync(() => fixture.Timetables.Reads >= 2);
        await scheduler.ProcessAsync(Monday);

        Assert.Equal(1, fixture.Presenter.Starts);
    }

    [Fact]
    public async Task StudentAnnouncementOptInsAreIndependentOfSelectedClasses()
    {
        Guid selectedClass = Guid.NewGuid();
        await AssertDeliveredAsync([SessionHalfDay.Am], AudienceType.SpecificClass, AudienceType.Am);
        await AssertDeliveredAsync([SessionHalfDay.Pm], AudienceType.SpecificClass, AudienceType.Pm);
        await AssertDeliveredAsync([], AudienceType.SpecificClass);

        async Task AssertDeliveredAsync(
            IEnumerable<SessionHalfDay> optedHalfDays,
            params AudienceType[] expectedAudiences)
        {
            SchedulerFixture fixture = CreateFixture(Monday);
            fixture.Audience.SetStudent([selectedClass], optedHalfDays);
            fixture.Announcements.Items =
            [
                AnnouncementFor(AudienceType.SpecificClass, selectedClass),
                AnnouncementFor(AudienceType.SpecificClass, Guid.NewGuid()),
                AnnouncementFor(AudienceType.Am),
                AnnouncementFor(AudienceType.Pm),
            ];
            using NotificationScheduler scheduler = fixture.Create();

            await scheduler.StartAsync();

            Assert.Equal(expectedAudiences, fixture.Presenter.AnnouncementAudiences);
        }

        Announcement AnnouncementFor(AudienceType audience, Guid? classId = null) =>
            new(Guid.NewGuid(), audience.ToString(), "Body", new DateTimeOffset(Monday), Guid.NewGuid(), new DateTimeOffset(Monday.AddHours(1)), audience, classId);
    }

    private static SchedulerFixture CreateFixture(DateTime now) => new(now);

    private sealed class SchedulerFixture
    {
        private readonly Guid _timetableId = Guid.NewGuid();
        private readonly Guid _periodId = Guid.NewGuid();
        public FakeTimetables Timetables { get; } = new();
        public FakeAnnouncements Announcements { get; } = new();
        public FakeOverrides Overrides { get; } = new();
        public FakeLog Log { get; } = new();
        public FakePresenter Presenter { get; } = new();
        public FakeSettings Settings { get; } = new();
        public FakeClasses Classes { get; } = new();
        public WeakReferenceMessenger Messenger { get; } = new();
        public DeviceAudienceContext Audience { get; }
        public FakeClock Clock { get; }
        public IWeekScheduleRepository Week { get; set; }

        public SchedulerFixture(DateTime now)
        {
            Clock = new FakeClock(now);
            Audience = new DeviceAudienceContext(Messenger);
            Week = new FakeWeek(new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Monday] = _timetableId }));
        }

        public void SetPeriod(TimeSpan starts, TimeSpan duration)
        {
            var start = TimeOnly.FromTimeSpan(starts);
            SetPeriods(new Period(_periodId, "Mathematics", start, start.Add(duration), 1));
        }

        public void SetPeriods(params Period[] periods) => Timetables.Items = [new Timetable(_timetableId, "Normal", false, periods)];

        public NotificationScheduler Create() => new(Timetables, Week, Overrides, Announcements, Classes, Audience, Log, Presenter, Settings, Clock, Messenger, NullLogger<NotificationScheduler>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class FakeTimetables : ITimetableRepository { public int Reads { get; private set; } public IReadOnlyList<Timetable> Items { get; set; } = []; public Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default) { Reads++; return Task.FromResult(Items); } }
    private sealed class FakeWeek(WeekSchedule value) : IWeekScheduleRepository { public Task<WeekSchedule> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(value); }
    private sealed class FakeOverrides : IDateOverrideRepository { public IReadOnlyList<DateOverride> Items { get; set; } = []; public Task<IReadOnlyList<DateOverride>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(Items); }
    private sealed class FakeClasses : IClassRepository
    {
        public IReadOnlySet<Guid> ClassIds { get; set; } = new HashSet<Guid>();
        public Task<IReadOnlyList<AqiClock.Domain.Entities.Class>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AqiClock.Domain.Entities.Class>>([]);
        public Task<IReadOnlySet<Guid>> GetClassIdsForPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) => Task.FromResult(ClassIds);
    }
    private sealed class FakeAnnouncements : IAnnouncementRepository { public IReadOnlyList<Announcement> Items { get; set; } = []; public Task<IReadOnlyList<Announcement>> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Announcement>>(Items.Where(item => !item.IsExpiredAt(now)).ToArray()); }
    private sealed class FakeClock(DateTime now) : IClock { public DateTime Now { get; set; } = now; public DateOnly LocalToday => DateOnly.FromDateTime(Now); }
    private sealed class FakeSettings : ISettingsService { public AppSettings Value { get; set; } = new(); public AppSettings Current => Value; public event EventHandler<SettingsChanged>? Changed; public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) { Value = settings; Changed?.Invoke(this, new(settings)); return Task.CompletedTask; } }
    private sealed class FakePresenter : INotificationPresenter
    {
        public int Starts { get; private set; } public int EndWarnings { get; private set; } public int Announcements { get; private set; }
        public List<IReadOnlyList<NotificationEvent>> StartGroups { get; } = [];
        public List<IReadOnlyList<NotificationEvent>> EndWarningGroups { get; } = [];
        public List<AudienceType> AnnouncementAudiences { get; } = [];
        public Task ShowLessonStartAsync(NotificationEvent notification, CancellationToken cancellationToken = default) { Starts++; return Task.CompletedTask; }
        public Task ShowLessonStartsAsync(IReadOnlyList<NotificationEvent> notifications, CancellationToken cancellationToken = default) { StartGroups.Add(notifications.ToArray()); Starts += notifications.Count; return Task.CompletedTask; }
        public Task ShowEndWarningAsync(NotificationEvent notification, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default) { EndWarnings++; return Task.CompletedTask; }
        public Task ShowEndWarningsAsync(IReadOnlyList<NotificationEvent> notifications, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default) { EndWarningGroups.Add(notifications.ToArray()); EndWarnings += notifications.Count; return Task.CompletedTask; }
        public Task ShowAnnouncementAsync(Announcement announcement, CancellationToken cancellationToken = default) { Announcements++; AnnouncementAudiences.Add(announcement.AudienceType); return Task.CompletedTask; }
        public Task ShowTestAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FakeLog : INotificationLogStore
    {
        public Dictionary<string, NotificationLogEntry> Entries { get; } = new(StringComparer.Ordinal);
        public Task<bool> ContainsAsync(string eventKey, CancellationToken cancellationToken = default) => Task.FromResult(Entries.ContainsKey(eventKey));
        public Task<NotificationLogEntry?> GetAsync(string eventKey, CancellationToken cancellationToken = default) => Task.FromResult(Entries.GetValueOrDefault(eventKey));
        public Task RecordAsync(string eventKey, DateTimeOffset? firedAt, bool skipped, CancellationToken cancellationToken = default) { Entries[eventKey] = new(eventKey, firedAt, skipped); return Task.CompletedTask; }
        public Task RemoveAsync(string eventKey, CancellationToken cancellationToken = default) { Entries.Remove(eventKey); return Task.CompletedTask; }
        public Task PruneAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
