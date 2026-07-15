namespace AqiClock.Domain.Entities;

/// <summary>
/// The default timetable assignment per weekday. A missing or null assignment means
/// no school on that weekday.
/// </summary>
public sealed class WeekSchedule
{
    public static WeekSchedule Empty { get; } = new(new Dictionary<DayOfWeek, Guid?>());

    private readonly Dictionary<DayOfWeek, Guid?> _assignments;

    public WeekSchedule(IReadOnlyDictionary<DayOfWeek, Guid?> assignments)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        _assignments = new Dictionary<DayOfWeek, Guid?>(assignments);
    }

    public Guid? TimetableIdFor(DayOfWeek weekday) =>
        _assignments.TryGetValue(weekday, out Guid? timetableId) ? timetableId : null;
}
