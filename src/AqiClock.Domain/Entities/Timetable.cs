namespace AqiClock.Domain.Entities;

/// <summary>A named single-day template of periods (e.g. "Normal Day", "Friday", "Ramadan Day").</summary>
public sealed record Timetable(
    Guid Id,
    string Name,
    bool IsArchived,
    IReadOnlyList<Period> Periods);
