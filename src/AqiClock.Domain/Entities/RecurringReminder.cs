namespace AqiClock.Domain.Entities;

public enum ReminderTiming { LessonEnd, FixedTime }

/// <summary>Weekly staff reminder. Weekday bits run Monday (1) through Sunday (64).</summary>
public sealed record RecurringReminder(
    Guid Id, string Title, string Body, int Weekdays, ReminderTiming Timing,
    TimeOnly? FixedTime, bool IsEnabled)
{
    public bool RunsOn(DayOfWeek day) => (Weekdays & (1 << (((int)day + 6) % 7))) != 0;
}
