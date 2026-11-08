using System.Globalization;
using AqiClock.Domain.Entities;

namespace AqiClock.Domain.Scheduling;

public sealed record ReminderOccurrence(string Key, RecurringReminder Reminder, DateTime TriggerTime);

public static class RecurringReminderEngine
{
    public static IReadOnlyList<ReminderOccurrence> GetOccurrences(
        ScheduleSnapshot snapshot, IEnumerable<RecurringReminder> reminders, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(reminders);
        var result = new List<ReminderOccurrence>();
        // Resolve the actual day, including replacements, closures and class-specific schedules.
        TimeOnly[] lessonEnds = ScheduleEngine.ResolveDay(snapshot, date).Periods
            .Where(period => period.IsLesson && period.IsValid)
            .Select(period => period.EndTime).Distinct().Order().ToArray();
        foreach (RecurringReminder reminder in reminders.Where(item => item.IsEnabled && item.RunsOn(date.DayOfWeek)))
        {
            IEnumerable<TimeOnly> times = reminder.Timing == ReminderTiming.LessonEnd ? lessonEnds
                : reminder.FixedTime is { } fixedTime ? [fixedTime] : [];
            foreach (TimeOnly time in times)
                result.Add(new ReminderOccurrence(
                    string.Create(CultureInfo.InvariantCulture, $"reminder:{reminder.Id:N}:{date:yyyy-MM-dd}:{time:HHmmss}"),
                    reminder, date.ToDateTime(time)));
        }
        return result.OrderBy(item => item.TriggerTime).ThenBy(item => item.Key, StringComparer.Ordinal).ToArray();
    }
}
