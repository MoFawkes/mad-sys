namespace AqiClock.Domain.Entities;

/// <summary>
/// One named block in a timetable (lesson, break, assembly, …) with wall-clock start
/// and end times on a single calendar day. Times never cross midnight.
/// </summary>
public sealed record Period(
    Guid Id,
    string Name,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SortOrder,
    bool IsLesson = true)
{
    /// <summary>An inverted or zero-length period is bad legacy data and is never active.</summary>
    public bool IsValid => EndTime > StartTime;

    public TimeSpan Duration => EndTime - StartTime;

    /// <summary>Active when start ≤ time &lt; end.</summary>
    public bool IsActiveAt(TimeOnly time) => IsValid && StartTime <= time && time < EndTime;
}
