using System.Globalization;
using AqiClock.Domain.Entities;

namespace AqiClock.Domain.Scheduling;

/// <summary>
/// Pure schedule computation over a <see cref="ScheduleSnapshot"/>. Normative rules:
/// ARCHITECTURE.md §4 (calculation) and §7 (notification event derivation),
/// docs/BUSINESS_RULES.md for the plain-language equivalents.
/// </summary>
public static class ScheduleEngine
{
    /// <summary>How far ahead the next-lesson search scans before giving up.</summary>
    public const int LookaheadDays = 60;

    /// <summary>Resolves the effective timetable for a date: override → week schedule → none.</summary>
    public static EffectiveDay ResolveDay(ScheduleSnapshot snapshot, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        (Timetable Timetable, Guid? ClassId)[] sources;
        EffectiveDaySource source;

        if (snapshot.FindOverride(date) is { } dateOverride)
        {
            source = EffectiveDaySource.Override;
            // A missing referenced timetable (cache inconsistency) is treated as closed.
            Timetable? timetable = dateOverride.TimetableId is { } overrideTimetableId ? snapshot.FindTimetable(overrideTimetableId) : null;
            sources = timetable is null ? [] : [(timetable, null)];
        }
        else
        {
            IReadOnlyList<WeekScheduleEntry> entries = snapshot.ResolveWeekEntries(date.DayOfWeek);
            sources = entries
                .Where(entry => entry.TimetableId is not null)
                .Select(entry => (Timetable: snapshot.FindTimetable(entry.TimetableId!.Value), entry.AudienceClassId))
                .Where(item => item.Timetable is not null)
                .Select(item => (item.Timetable!, item.AudienceClassId))
                .GroupBy(item => item.Item1.Id)
                .Select(group =>
                {
                    Guid?[] classIds = group.Select(item => item.AudienceClassId).Distinct().ToArray();
                    return (group.First().Item1, ClassId: classIds.Length == 1 ? classIds[0] : null);
                })
                .ToArray();
            source = entries.Any(entry => entry.TimetableId is not null) ? EffectiveDaySource.WeekSchedule : EffectiveDaySource.None;
        }

        ScheduledPeriod[] scheduled = SortedValidPeriods(sources);
        return new EffectiveDay(date, sources.Length == 0 ? null : sources[0].Timetable, source, scheduled.Select(item => item.Period).ToArray())
        {
            Timetables = sources.Select(item => item.Timetable).ToArray(),
            ScheduledPeriods = scheduled,
        };
    }

    /// <summary>Computes the full display state (current period, next period, day) at one instant.</summary>
    public static LessonStatus GetStatus(ScheduleSnapshot snapshot, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        EffectiveDay day = ResolveDay(snapshot, DateOnly.FromDateTime(now));
        PeriodOccurrence? current = FindCurrentPeriod(day, TimeOnly.FromDateTime(now));
        PeriodOccurrence? next = FindNextPeriod(snapshot, now);
        return new LessonStatus(now, day, current, next);
    }

    /// <summary>
    /// The period active at <paramref name="time"/> (start ≤ time &lt; end). When periods
    /// overlap, the one with the latest start wins; ties go to the lowest sort order.
    /// </summary>
    public static PeriodOccurrence? FindCurrentPeriod(EffectiveDay day, TimeOnly time)
    {
        ArgumentNullException.ThrowIfNull(day);

        Period? best = null;
        foreach (Period period in day.Periods)
        {
            if (!period.IsActiveAt(time))
            {
                continue;
            }

            if (best is null
                || period.StartTime > best.StartTime
                || (period.StartTime == best.StartTime && period.SortOrder < best.SortOrder))
            {
                best = period;
            }
        }

        return best is null ? null : new PeriodOccurrence(day.Date, best);
    }

    /// <summary>
    /// The next period strictly after <paramref name="after"/>: the earliest remaining start
    /// today, otherwise the first period of the next school day within <see cref="LookaheadDays"/>.
    /// Returns null when nothing is scheduled in the window.
    /// </summary>
    public static PeriodOccurrence? FindNextPeriod(ScheduleSnapshot snapshot, DateTime after)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var date = DateOnly.FromDateTime(after);
        var time = TimeOnly.FromDateTime(after);

        EffectiveDay today = ResolveDay(snapshot, date);
        Period? nextToday = today.Periods.FirstOrDefault(p => p.StartTime > time);
        if (nextToday is not null)
        {
            return new PeriodOccurrence(date, nextToday);
        }

        for (int daysAhead = 1; daysAhead <= LookaheadDays; daysAhead++)
        {
            EffectiveDay futureDay = ResolveDay(snapshot, date.AddDays(daysAhead));
            if (futureDay.IsSchoolDay)
            {
                return new PeriodOccurrence(futureDay.Date, futureDay.Periods[0]);
            }
        }

        return null;
    }

    /// <summary>
    /// Derives the notification events for one date: a start event per period, plus an
    /// end-warning event <paramref name="endWarningLead"/> before each period's end.
    /// End warnings are suppressed for periods no longer than the lead itself, and
    /// entirely when the lead is zero. Closed days yield no events.
    /// </summary>
    public static IReadOnlyList<NotificationEvent> GetNotificationEvents(
        ScheduleSnapshot snapshot,
        DateOnly date,
        TimeSpan endWarningLead)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfLessThan(endWarningLead, TimeSpan.Zero);

        EffectiveDay day = ResolveDay(snapshot, date);
        var events = new List<NotificationEvent>();
        foreach (Period period in day.Periods)
        {
            var occurrence = new PeriodOccurrence(date, period);
            events.Add(new NotificationEvent(
                EventKey("start", period.Id, date),
                NotificationEventKind.LessonStart,
                occurrence,
                occurrence.StartsAt));

            if (endWarningLead > TimeSpan.Zero && period.Duration > endWarningLead)
            {
                events.Add(new NotificationEvent(
                    EventKey("end-warning", period.Id, date),
                    NotificationEventKind.EndWarning,
                    occurrence,
                    occurrence.EndsAt - endWarningLead));
            }
        }

        return events
            .OrderBy(e => e.TriggerTime)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string EventKey(string kind, Guid periodId, DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{kind}:{periodId:N}:{date:yyyy-MM-dd}");

    private static ScheduledPeriod[] SortedValidPeriods(IReadOnlyList<(Timetable Timetable, Guid? ClassId)> sources) =>
        sources.SelectMany(source => source.Timetable.Periods
                .Where(period => period.IsValid)
                .Select(period => new ScheduledPeriod(period, source.ClassId)))
            .OrderBy(item => item.Period.StartTime)
            .ThenBy(item => item.Period.SortOrder)
            .ThenBy(item => item.Period.Id)
            .ToArray();
}
