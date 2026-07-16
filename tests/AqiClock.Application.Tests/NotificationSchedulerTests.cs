using AqiClock.Application.Abstractions;
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

    private static SchedulerFixture CreateFixture(DateTime now) => new(now);

    private sealed class SchedulerFixture
    {
        private readonly Guid _timetableId = Guid.NewGuid();
        private readonly Guid _periodId = Guid.NewGuid();
        public FakeTimetables Timetables { get; } = new();
        public FakeAnnouncements Announcements { get; } = new();
        public FakeLog Log { get; } = new();
        public FakePresenter Presenter { get; } = new();
        public FakeSettings Settings { get; } = new();
        public FakeClock Clock { get; }
        public IWeekScheduleRepository Week { get; }

        public SchedulerFixture(DateTime now)
        {
            Clock = new FakeClock(now);
            Week = new FakeWeek(new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Monday] = _timetableId }));
        }

        public void SetPeriod(TimeSpan starts, TimeSpan duration)
        {
            var start = TimeOnly.FromTimeSpan(starts);
            Timetables.Items = [new Timetable(_timetableId, "Normal", false, [new Period(_periodId, "Mathematics", start, start.Add(duration), 1)])];
        }

        public NotificationScheduler Create() => new(Timetables, Week, new FakeOverrides(), Announcements, Log, Presenter, Settings, Clock, new WeakReferenceMessenger(), NullLogger<NotificationScheduler>.Instance);
    }

    private sealed class FakeTimetables : ITimetableRepository { public IReadOnlyList<Timetable> Items { get; set; } = []; public Task<IReadOnlyList<Timetable>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(Items); }
    private sealed class FakeWeek(WeekSchedule value) : IWeekScheduleRepository { public Task<WeekSchedule> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(value); }
    private sealed class FakeOverrides : IDateOverrideRepository { public Task<IReadOnlyList<DateOverride>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DateOverride>>([]); }
    private sealed class FakeAnnouncements : IAnnouncementRepository { public IReadOnlyList<Announcement> Items { get; set; } = []; public Task<IReadOnlyList<Announcement>> GetCurrentAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Announcement>>(Items.Where(item => !item.IsExpiredAt(now)).ToArray()); }
    private sealed class FakeClock(DateTime now) : IClock { public DateTime Now { get; set; } = now; public DateOnly LocalToday => DateOnly.FromDateTime(Now); }
    private sealed class FakeSettings : ISettingsService { public AppSettings Value { get; set; } = new(); public AppSettings Current => Value; public event EventHandler<SettingsChanged>? Changed; public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask; public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) { Value = settings; Changed?.Invoke(this, new(settings)); return Task.CompletedTask; } }
    private sealed class FakePresenter : INotificationPresenter
    {
        public int Starts { get; private set; } public int EndWarnings { get; private set; } public int Announcements { get; private set; }
        public Task ShowLessonStartAsync(NotificationEvent notification, int periodNumber, CancellationToken cancellationToken = default) { Starts++; return Task.CompletedTask; }
        public Task ShowEndWarningAsync(NotificationEvent notification, PeriodOccurrence? followingPeriod, int warningMinutes, CancellationToken cancellationToken = default) { EndWarnings++; return Task.CompletedTask; }
        public Task ShowAnnouncementAsync(Announcement announcement, CancellationToken cancellationToken = default) { Announcements++; return Task.CompletedTask; }
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
