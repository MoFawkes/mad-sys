using AqiClock.Domain.Entities;

namespace AqiClock.Domain.Scheduling;

/// <summary>
/// An immutable in-memory view of all schedule data (as pulled from the local cache).
/// The engine only ever computes over one of these; a data change means building a
/// new snapshot and recomputing — the engine itself holds no state.
/// </summary>
public sealed class ScheduleSnapshot
{
    public static ScheduleSnapshot Empty { get; } = new([], WeekSchedule.Empty, []);

    private readonly Dictionary<Guid, Timetable> _timetablesById;
    private readonly Dictionary<DateOnly, DateOverride> _overridesByDate;
    private readonly WeekScheduleEntry?[] _resolvedWeek = new WeekScheduleEntry?[7];

    public WeekSchedule WeekSchedule { get; }
    public IReadOnlySet<Guid> ViewerClassIds { get; }

    public ScheduleSnapshot(
        IEnumerable<Timetable> timetables,
        WeekSchedule weekSchedule,
        IEnumerable<DateOverride> dateOverrides,
        IReadOnlySet<Guid>? viewerClassIds = null)
    {
        ArgumentNullException.ThrowIfNull(timetables);
        ArgumentNullException.ThrowIfNull(weekSchedule);
        ArgumentNullException.ThrowIfNull(dateOverrides);

        _timetablesById = timetables.ToDictionary(t => t.Id);
        WeekSchedule = weekSchedule;
        ViewerClassIds = viewerClassIds ?? new HashSet<Guid>();
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            _resolvedWeek[(int)day] = weekSchedule.ResolveFor(day, ViewerClassIds);

        // The server enforces one override per date; tolerate duplicates defensively (last wins).
        _overridesByDate = [];
        foreach (DateOverride dateOverride in dateOverrides)
        {
            _overridesByDate[dateOverride.Date] = dateOverride;
        }
    }

    public Timetable? FindTimetable(Guid timetableId) => _timetablesById.GetValueOrDefault(timetableId);

    public DateOverride? FindOverride(DateOnly date) => _overridesByDate.GetValueOrDefault(date);
    public WeekScheduleEntry? ResolveWeekEntry(DayOfWeek weekday) => _resolvedWeek[(int)weekday];
}
