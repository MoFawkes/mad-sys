using System.Collections.ObjectModel;
using System.Globalization;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using AqiClock.Domain.Time;
using AqiClock.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.App.ViewModels;

public sealed record PeriodDisplay(string Name, string Time, bool IsCurrent, bool IsPast, string? ClassName = null)
{
    public bool IsUpcoming => !IsCurrent && !IsPast;
    public bool HasClassName => !string.IsNullOrWhiteSpace(ClassName);
}

public sealed record PeriodGroupDisplay(string Time, IReadOnlyList<PeriodDisplay> Periods)
{
    public bool IsCurrent => Periods.Any(period => period.IsCurrent);
    public bool IsPast => Periods.All(period => period.IsPast);
    public bool HasSharedName => Periods.Count > 1 && Periods.All(period =>
        string.Equals(period.Name, Periods[0].Name, StringComparison.OrdinalIgnoreCase));
    public string Name => HasSharedName ? Periods[0].Name : string.Empty;
}

public partial class ClockViewModel : ObservableObject, IRecipient<ClockTick>, IRecipient<TimeJumped>, IRecipient<DataChanged>, IRecipient<AudienceChanged>
{
    private readonly ITimetableRepository _timetables;
    private readonly IWeekScheduleRepository _weekSchedule;
    private readonly IDateOverrideRepository _overrides;
    private readonly IDeviceAudienceContext _audience;
    private readonly IClock _clock;
    private readonly IClassRepository? _classes;
    private IReadOnlyDictionary<Guid, string> _classNames = new Dictionary<Guid, string>();
    private IReadOnlyDictionary<Guid, int> _classOrder = new Dictionary<Guid, int>();
    private ScheduleSnapshot _snapshot = ScheduleSnapshot.Empty;

    [ObservableProperty] private string _timeText = "--:--:--";
    [ObservableProperty] private string _shortTimeText = "--:--";
    [ObservableProperty] private string _dateText = string.Empty;
    [ObservableProperty] private string _currentLesson = "No lessons today";
    [ObservableProperty] private string _currentDetail = string.Empty;
    [ObservableProperty] private string _remaining = string.Empty;
    [ObservableProperty] private string _compactLessonDetail = "No lessons today";
    [ObservableProperty] private string _nextLesson = "No upcoming lessons";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _hasCurrentLesson;
    [ObservableProperty] private bool _isNaseehah;
    [ObservableProperty] private bool _isTeachingLesson;
    [ObservableProperty] private bool _hasPeriods;
    [ObservableProperty] private string _timeZoneNote = string.Empty;

    public ObservableCollection<PeriodDisplay> TodayPeriods { get; } = [];
    public ObservableCollection<PeriodGroupDisplay> TodayPeriodGroups { get; } = [];

    public ClockViewModel(ITimetableRepository timetables, IWeekScheduleRepository weekSchedule, IDateOverrideRepository overrides, IDeviceAudienceContext audience, IMessenger messenger, IClock? clock = null, IClassRepository? classes = null)
    {
        _timetables = timetables; _weekSchedule = weekSchedule; _overrides = overrides; _audience = audience; _clock = clock ?? DeviceClock.Instance; _classes = classes;
        messenger.Register<ClockTick>(this); messenger.Register<TimeJumped>(this); messenger.Register<DataChanged>(this); messenger.Register<AudienceChanged>(this);
    }

    public ClockViewModel(ITimetableRepository timetables, IWeekScheduleRepository weekSchedule, IDateOverrideRepository overrides, IMessenger messenger)
        : this(timetables, weekSchedule, overrides, new DeviceAudienceContext(messenger), messenger) { }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Timetable> timetables = await _timetables.GetAllAsync(cancellationToken);
        WeekSchedule schedule = await _weekSchedule.GetAsync(cancellationToken);
        IReadOnlyList<DateOverride> overrides = await _overrides.GetAllAsync(cancellationToken);
        _snapshot = new ScheduleSnapshot(timetables, schedule, overrides, _audience.Current.SelectedClassIds);
        if (_classes is not null)
        {
            IReadOnlyList<Class> classes = await _classes.GetAllAsync(cancellationToken);
            _classNames = classes.ToDictionary(item => item.Id, item => item.Name);
            _classOrder = classes.ToDictionary(item => item.Id, item => item.SortOrder);
        }
    }

    public void Receive(ClockTick message) => Recompute(message.Now);
    public void Receive(TimeJumped message) => Recompute(message.Current);
    public void Receive(DataChanged message)
    {
        if (message.Table is CacheTable.Timetables or CacheTable.Periods or CacheTable.WeekSchedule or CacheTable.DateOverrides)
            UiDispatch.Run(ReloadAsync);
    }
    public void Receive(AudienceChanged message) => UiDispatch.Run(ReloadAsync);

    private async Task ReloadAsync() { await LoadAsync(); Recompute(_clock.Now); }

    private void Recompute(DateTime now)
    {
        TimeText = now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        ShortTimeText = now.ToString("HH:mm", CultureInfo.CurrentCulture);
        DateText = now.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
        TimeZoneNote = _clock is IInstituteClock institute && institute.DiffersFromDeviceZone
            ? $"Institute time ({institute.TimeZoneId}) · your time {institute.DeviceNow:HH:mm}"
            : string.Empty;
        LessonStatus status = ScheduleEngine.GetStatus(_snapshot, now);
        HasCurrentLesson = status.Current is not null;
        IsNaseehah = status.Current?.Period.Name.Contains("Naseeha", StringComparison.OrdinalIgnoreCase) == true;
        IsTeachingLesson = status.Current?.Period.IsLesson == true && !IsNaseehah;
        CurrentLesson = status.Current?.Period.Name ?? (status.Day.IsSchoolDay ? "No lesson right now" : "No lessons today");
        CurrentDetail = status.Current is null ? string.Empty : $"Ends at {status.Current.Period.EndTime:HH:mm}";
        Remaining = status.TimeRemaining is { } remaining ? FormatDuration(remaining) : string.Empty;
        CompactLessonDetail = status.TimeRemaining is { } compactRemaining
            ? $"{CurrentLesson} · {Math.Max(1, (int)Math.Ceiling(compactRemaining.TotalMinutes))} min left"
            : CurrentLesson;
        Progress = (status.Progress ?? 0d) * 100d;
        NextLesson = status.Next is { } next
            ? FormatNextLesson(next, DateOnly.FromDateTime(now))
            : "No upcoming lessons";
        TodayPeriods.Clear();
        TodayPeriodGroups.Clear();
        var rows = new List<(TimeOnly Start, TimeOnly End, Guid? ClassId, PeriodDisplay Display)>();
        TimeOnly time = TimeOnly.FromDateTime(now);
        bool showClasses = _audience.Current.SelectedClassIds.Count > 1;
        foreach (ScheduledPeriod scheduled in status.Day.ScheduledPeriods)
        {
            Period period = scheduled.Period;
            string? className = showClasses && scheduled.ClassId is { } classId ? _classNames.GetValueOrDefault(classId) : null;
            var display = new PeriodDisplay(period.Name, $"{period.StartTime:HH:mm}–{period.EndTime:HH:mm}", status.Current?.Period.Id == period.Id, period.EndTime <= time, className);
            TodayPeriods.Add(display);
            rows.Add((period.StartTime, period.EndTime, scheduled.ClassId, display));
        }
        foreach (var group in rows.GroupBy(row => (row.Start, row.End)))
            TodayPeriodGroups.Add(new(group.First().Display.Time, group
                .OrderBy(row => row.ClassId is { } classId ? _classOrder.GetValueOrDefault(classId, int.MaxValue) : int.MaxValue)
                .ThenBy(row => row.ClassId?.ToString("D"), StringComparer.Ordinal)
                .Select(row => row.Display).ToArray()));
        HasPeriods = TodayPeriods.Count > 0;
    }

    public static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes:00}:{duration.Seconds:00}");

    private static string FormatNextLesson(PeriodOccurrence next, DateOnly today)
    {
        string day = next.Date == today ? string.Empty : $"{next.Date:dddd}, ";
        return $"Next: {day}{next.Period.Name} at {next.Period.StartTime:HH:mm}";
    }

    private sealed class DeviceClock : IClock
    {
        public static DeviceClock Instance { get; } = new();
        public DateTime Now => DateTime.Now;
        public DateOnly LocalToday => DateOnly.FromDateTime(Now);
    }
}
