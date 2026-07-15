namespace AqiClock.Domain.Entities;

/// <summary>
/// A calendar-date exception that takes precedence over the week schedule.
/// A null <paramref name="TimetableId"/> means the school is closed on that date.
/// </summary>
public sealed record DateOverride(
    Guid Id,
    DateOnly Date,
    Guid? TimetableId,
    string? Note = null)
{
    public bool IsClosed => TimetableId is null;
}
