using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;

namespace AqiClock.Domain.Tests.Scheduling;

public sealed class RecurringReminderTests
{
    private static readonly DateOnly Friday = new(2026, 11, 13);
    private static readonly RecurringReminder Reminder = new(Guid.NewGuid(), "Week chart", "Fill in the weekly chart", 16, ReminderTiming.LessonEnd, null, true);

    [Fact]
    public void FridayOnlyIncludesLessonsAndDeduplicatesSimultaneousEnds()
    {
        Timetable timetable = Day();
        ScheduleSnapshot snapshot = Snapshot(timetable);
        IReadOnlyList<ReminderOccurrence> events = RecurringReminderEngine.GetOccurrences(snapshot, [Reminder], Friday);
        Assert.Equal([Friday.ToDateTime(new(10, 0)), Friday.ToDateTime(new(11, 0))], events.Select(item => item.TriggerTime));
        Assert.Equal(2, events.Select(item => item.Key).Distinct().Count());
        Assert.Empty(RecurringReminderEngine.GetOccurrences(snapshot, [Reminder], Friday.AddDays(1)));
        Assert.Empty(RecurringReminderEngine.GetOccurrences(snapshot, [Reminder with { IsEnabled = false }], Friday));
    }

    [Fact]
    public void ClosureAndReplacementControlLessonEnds()
    {
        Timetable timetable = Day();
        var closed = Snapshot(timetable, new DateOverride(Guid.NewGuid(), Friday, null, "Holiday"));
        Assert.Empty(RecurringReminderEngine.GetOccurrences(closed, [Reminder], Friday));
        Timetable replacement = timetable with { Id = Guid.NewGuid(), Periods = [new(Guid.NewGuid(), "Short lesson", new(8, 0), new(8, 30), 0)] };
        var changed = new ScheduleSnapshot([timetable, replacement], WeekSchedule.Empty,
            [new(Guid.NewGuid(), Friday, replacement.Id, null)]);
        Assert.Equal(Friday.ToDateTime(new(8, 30)), Assert.Single(RecurringReminderEngine.GetOccurrences(changed, [Reminder], Friday)).TriggerTime);
    }

    [Fact]
    public void FixedTimeRemindersAreCalendarBasedAndKeepDifferentMessagesSeparate()
    {
        var first = Reminder with { Timing = ReminderTiming.FixedTime, FixedTime = new(16, 0) };
        var second = first with { Id = Guid.NewGuid(), Title = "Another message" };
        IReadOnlyList<ReminderOccurrence> events = RecurringReminderEngine.GetOccurrences(ScheduleSnapshot.Empty, [first, second], Friday);
        Assert.Equal(2, events.Count);
        Assert.All(events, item => Assert.Equal(Friday.ToDateTime(new(16, 0)), item.TriggerTime));
        Assert.NotEqual(events[0].Key, events[1].Key);
        Assert.NotEqual(events[0].Key, Assert.Single(RecurringReminderEngine.GetOccurrences(ScheduleSnapshot.Empty, [first], Friday.AddDays(7))).Key);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, 1)]
    [InlineData(DayOfWeek.Friday, 16)]
    [InlineData(DayOfWeek.Sunday, 64)]
    public void WeekdayMaskUsesMondayFirst(DayOfWeek day, int mask)
    {
        var reminder = Reminder with { Weekdays = mask };
        Assert.True(reminder.RunsOn(day));
        Assert.False(reminder.RunsOn((DayOfWeek)(((int)day + 1) % 7)));
    }

    private static Timetable Day() => new(Guid.NewGuid(), "Normal", false,
        [new(Guid.NewGuid(), "Lesson 1", new(9, 0), new(10, 0), 0),
         new(Guid.NewGuid(), "Parallel lesson", new(9, 0), new(10, 0), 1),
         new(Guid.NewGuid(), "Break", new(10, 0), new(10, 15), 2, false),
         new(Guid.NewGuid(), "Lesson 2", new(10, 15), new(11, 0), 3)]);
    private static ScheduleSnapshot Snapshot(Timetable timetable, params DateOverride[] overrides) => new([timetable],
        new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Friday] = timetable.Id, [DayOfWeek.Saturday] = timetable.Id }), overrides);
}
