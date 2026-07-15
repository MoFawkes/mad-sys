using System.Globalization;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using static AqiClock.Domain.Tests.Scheduling.TestData;

namespace AqiClock.Domain.Tests.Scheduling;

public sealed class NotificationEventTests
{
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    [Fact]
    public void EveryPeriodGetsAStartEventIncludingBreaks()
    {
        IReadOnlyList<NotificationEvent> events =
            ScheduleEngine.GetNotificationEvents(WeekOf(NormalDay()), Monday, FiveMinutes);

        string[] startNames = events
            .Where(e => e.Kind == NotificationEventKind.LessonStart)
            .Select(e => e.Occurrence.Period.Name)
            .ToArray();
        string[] expected = ["Period 1", "Break", "Period 2"];
        Assert.Equal(expected, startNames);
    }

    [Fact]
    public void EndWarningFiresLeadMinutesBeforeTheEnd()
    {
        IReadOnlyList<NotificationEvent> events =
            ScheduleEngine.GetNotificationEvents(WeekOf(NormalDay()), Monday, FiveMinutes);

        NotificationEvent warning = events.Single(e =>
            e.Kind == NotificationEventKind.EndWarning && e.Occurrence.Period.Name == "Period 1");
        Assert.Equal(At(Monday, "09:55"), warning.TriggerTime);
    }

    [Fact]
    public void EndWarningIsSuppressedForPeriodsNoLongerThanTheLead()
    {
        // Break is 20 minutes: warned. A 5-minute and a 4-minute period with a
        // 5-minute lead: suppressed (duration ≤ lead).
        Timetable timetable = Tt("Short blocks",
            P("Five", "09:00", "09:05", sort: 1),
            P("Four", "09:10", "09:14", sort: 2),
            P("Six", "09:20", "09:26", sort: 3));

        IReadOnlyList<NotificationEvent> events =
            ScheduleEngine.GetNotificationEvents(WeekOf(timetable), Monday, FiveMinutes);

        string[] warned = events
            .Where(e => e.Kind == NotificationEventKind.EndWarning)
            .Select(e => e.Occurrence.Period.Name)
            .ToArray();
        string[] expected = ["Six"];
        Assert.Equal(expected, warned);
    }

    [Fact]
    public void ZeroLeadDisablesAllEndWarnings()
    {
        IReadOnlyList<NotificationEvent> events =
            ScheduleEngine.GetNotificationEvents(WeekOf(NormalDay()), Monday, TimeSpan.Zero);

        Assert.DoesNotContain(events, e => e.Kind == NotificationEventKind.EndWarning);
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public void NegativeLeadIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScheduleEngine.GetNotificationEvents(WeekOf(NormalDay()), Monday, TimeSpan.FromMinutes(-1)));

    [Fact]
    public void ClosedDayYieldsNoEvents()
    {
        ScheduleSnapshot snapshot = WeekOf(NormalDay(),
            new DateOverride(Guid.NewGuid(), Monday, TimetableId: null));

        Assert.Empty(ScheduleEngine.GetNotificationEvents(snapshot, Monday, FiveMinutes));
    }

    [Fact]
    public void EventsAreOrderedByTriggerTime()
    {
        IReadOnlyList<NotificationEvent> events =
            ScheduleEngine.GetNotificationEvents(WeekOf(NormalDay()), Monday, FiveMinutes);

        Assert.Equal(events.OrderBy(e => e.TriggerTime).Select(e => e.Key), events.Select(e => e.Key));
    }

    [Fact]
    public void KeyFormatIsStableAndDateScoped()
    {
        var periodId = Guid.NewGuid();
        Timetable timetable = Tt("One", P("Only", "09:00", "10:00", id: periodId));

        IReadOnlyList<NotificationEvent> events =
            ScheduleEngine.GetNotificationEvents(WeekOf(timetable), Monday, FiveMinutes);

        string idText = periodId.ToString("N", CultureInfo.InvariantCulture);
        Assert.Equal($"start:{idText}:2026-07-13", events[0].Key);
        Assert.Equal($"end-warning:{idText}:2026-07-13", events[1].Key);
    }

    [Fact]
    public void SamePeriodOnDifferentDatesGetsDifferentKeys()
    {
        ScheduleSnapshot snapshot = WeekOf(NormalDay());

        string mondayKey = ScheduleEngine.GetNotificationEvents(snapshot, Monday, FiveMinutes)[0].Key;
        string tuesdayKey = ScheduleEngine.GetNotificationEvents(snapshot, Tuesday, FiveMinutes)[0].Key;

        Assert.NotEqual(mondayKey, tuesdayKey);
    }

    [Fact]
    public void OverlappingPeriodsBothGetTheirOwnEvents()
    {
        Timetable overlapping = Tt("Overlap",
            P("Long block", "09:00", "12:00", sort: 1),
            P("Intervention", "10:00", "10:30", sort: 2));

        IReadOnlyList<NotificationEvent> events =
            ScheduleEngine.GetNotificationEvents(WeekOf(overlapping), Monday, FiveMinutes);

        Assert.Equal(4, events.Count); // 2 starts + 2 end-warnings
    }
}
