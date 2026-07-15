using AqiClock.Domain.Entities;

namespace AqiClock.Domain.Scheduling;

/// <summary>Which precedence rule produced the effective timetable for a date.</summary>
public enum EffectiveDaySource
{
    /// <summary>No override and no week-schedule assignment: no school.</summary>
    None = 0,

    /// <summary>The weekday's default assignment applied.</summary>
    WeekSchedule = 1,

    /// <summary>A date override applied (its timetable may be null: closed).</summary>
    Override = 2,
}

/// <summary>
/// The resolved timetable for one calendar date. <see cref="Periods"/> contains only
/// valid periods (end &gt; start), ordered by start time then sort order; it is empty
/// on closed days, unassigned days, and days whose referenced timetable is missing
/// from the snapshot.
/// </summary>
public sealed record EffectiveDay(
    DateOnly Date,
    Timetable? Timetable,
    EffectiveDaySource Source,
    IReadOnlyList<Period> Periods)
{
    public bool IsSchoolDay => Periods.Count > 0;
}
