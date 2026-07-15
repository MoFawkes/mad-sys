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

    public WeekSchedule WeekSchedule { get; }

    public ScheduleSnapshot(
        IEnumerable<Timetable> timetables,
        WeekSchedule weekSchedule,
        IEnumerable<DateOverride> dateOverrides)
    {
        ArgumentNullException.ThrowIfNull(timetables);
        ArgumentNullException.ThrowIfNull(weekSchedule);
        ArgumentNullException.ThrowIfNull(dateOverrides);

        _timetablesById = timetables.ToDictionary(t => t.Id);
        WeekSchedule = weekSchedule;

        // The server enforces one override per date; tolerate duplicates defensively (last wins).
        _overridesByDate = [];
        foreach (DateOverride dateOverride in dateOverrides)
        {
            _overridesByDate[dateOverride.Date] = dateOverride;
        }
    }

    public Timetable? FindTimetable(Guid timetableId) => _timetablesById.GetValueOrDefault(timetableId);

    public DateOverride? FindOverride(DateOnly date) => _overridesByDate.GetValueOrDefault(date);
}
