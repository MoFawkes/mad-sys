using System.Collections.ObjectModel;
using System.Globalization;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace AqiClock.App.ViewModels;

public sealed record PeriodDisplay(string Name, string Time, bool IsCurrent, bool IsPast);

public partial class ClockViewModel : ObservableObject, IRecipient<ClockTick>, IRecipient<TimeJumped>, IRecipient<DataChanged>
{
    private readonly ITimetableRepository _timetables;
    private readonly IWeekScheduleRepository _weekSchedule;
    private readonly IDateOverrideRepository _overrides;
    private ScheduleSnapshot _snapshot = ScheduleSnapshot.Empty;

    [ObservableProperty] private string _timeText = "--:--:--";
    [ObservableProperty] private string _dateText = string.Empty;
    [ObservableProperty] private string _currentLesson = "No lessons today";
    [ObservableProperty] private string _currentDetail = string.Empty;
    [ObservableProperty] private string _remaining = string.Empty;
    [ObservableProperty] private string _nextLesson = "No upcoming lessons";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _hasCurrentLesson;

    public ObservableCollection<PeriodDisplay> TodayPeriods { get; } = [];

    public ClockViewModel(ITimetableRepository timetables, IWeekScheduleRepository weekSchedule, IDateOverrideRepository overrides, IMessenger messenger)
    {
        _timetables = timetables; _weekSchedule = weekSchedule; _overrides = overrides;
        messenger.Register<ClockTick>(this); messenger.Register<TimeJumped>(this); messenger.Register<DataChanged>(this);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Timetable> timetables = await _timetables.GetAllAsync(cancellationToken);
        WeekSchedule schedule = await _weekSchedule.GetAsync(cancellationToken);
        IReadOnlyList<DateOverride> overrides = await _overrides.GetAllAsync(cancellationToken);
        _snapshot = new ScheduleSnapshot(timetables, schedule, overrides);
    }

    public void Receive(ClockTick message) => Recompute(message.Now);
    public void Receive(TimeJumped message) => Recompute(message.Current);
    public void Receive(DataChanged message)
    {
        if (message.Table is CacheTable.Timetables or CacheTable.Periods or CacheTable.WeekSchedule or CacheTable.DateOverrides)
            _ = ReloadAsync();
    }

    private async Task ReloadAsync() { await LoadAsync(); Recompute(DateTime.Now); }

    private void Recompute(DateTime now)
    {
        TimeText = now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        DateText = now.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture);
        LessonStatus status = ScheduleEngine.GetStatus(_snapshot, now);
        HasCurrentLesson = status.Current is not null;
        CurrentLesson = status.Current?.Period.Name ?? (status.Day.IsSchoolDay ? "No lesson right now" : "No lessons today");
        CurrentDetail = status.Current is null ? string.Empty : $"Ends {status.Current.Period.EndTime:HH:mm}";
        Remaining = status.TimeRemaining is { } remaining ? FormatDuration(remaining) : string.Empty;
        Progress = (status.Progress ?? 0d) * 100d;
        NextLesson = status.Next is { } next ? $"Next: {next.Period.Name} at {next.Period.StartTime:HH:mm}" : "No upcoming lessons";
        TodayPeriods.Clear();
        TimeOnly time = TimeOnly.FromDateTime(now);
        foreach (Period period in status.Day.Periods)
            TodayPeriods.Add(new PeriodDisplay(period.Name, $"{period.StartTime:HH:mm}–{period.EndTime:HH:mm}", status.Current?.Period.Id == period.Id, period.EndTime <= time));
    }

    public static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{duration.Minutes:00}:{duration.Seconds:00}");
}
