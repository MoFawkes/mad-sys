namespace AqiClock.Domain.Scheduling;

public enum NotificationEventKind
{
    LessonStart = 0,
    EndWarning = 1,
}

/// <summary>
/// A derived point-in-time notification for one period on one date. <see cref="Key"/> is
/// the stable dedup identity ("start:{periodId:N}:{yyyy-MM-dd}") persisted in the local
/// notification log — it deliberately excludes the trigger time, so a rescheduled period
/// keeps its identity (firing semantics for moved periods live in the scheduler, ARCHITECTURE.md §7).
/// </summary>
public sealed record NotificationEvent(
    string Key,
    NotificationEventKind Kind,
    PeriodOccurrence Occurrence,
    DateTime TriggerTime);
