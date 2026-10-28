using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;

namespace AqiClock.Domain.Tests.Scheduling;

public sealed class NaseehahNotificationTests
{
    private static readonly DateOnly Monday = new(2026, 7, 20);

    [Fact]
    public void UsesOnlyAssignedTimetablesAndDeduplicatesSharedStarts()
    {
        Timetable[] timetables = [Day("Break / Naseehah", 11), Day("NASEEHAH & Break", 11),
            Day("Isha + Naseehah", 19), Day("Unassigned Naseehah", 10), Day("Mathematics", 9)];
        var week = new WeekSchedule(timetables.Where(t => t.Name != "Unassigned Naseehah")
            .Select(t => new WeekScheduleEntry(Guid.NewGuid(), DayOfWeek.Monday, Guid.NewGuid(), t.Id)));
        var snapshot = new ScheduleSnapshot(timetables, week, [], new HashSet<Guid> { Guid.NewGuid() });
        IReadOnlyList<NotificationEvent> events = ScheduleEngine.GetNaseehahStartEvents(snapshot, Monday);
        Assert.Equal(["naseehah:2026-07-20:1100", "naseehah:2026-07-20:1900"], events.Select(e => e.Key));
        Assert.Empty(ScheduleEngine.GetNaseehahStartEvents(snapshot, Monday.AddDays(1)));
    }

    [Fact]
    public void DateOverrideReplacesAllSharedSourcesIncludingClosure()
    {
        Timetable normal = Day("Naseehah", 11);
        Timetable replacement = Day("Isha + Naseehah", 18);
        var week = new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Monday] = normal.Id });
        var closed = new ScheduleSnapshot([normal, replacement], week,
            [new(Guid.NewGuid(), Monday, null, "Closed")]);
        Assert.Empty(ScheduleEngine.GetNaseehahStartEvents(closed, Monday));
        var changed = new ScheduleSnapshot([normal, replacement], week,
            [new(Guid.NewGuid(), Monday, replacement.Id, null)]);
        Assert.Equal("naseehah:2026-07-20:1800", Assert.Single(ScheduleEngine.GetNaseehahStartEvents(changed, Monday)).Key);
    }

    private static Timetable Day(string name, int hour) => new(Guid.NewGuid(), name, false,
        [new Period(Guid.NewGuid(), name, new(hour, 0), new(hour, 25), 0, false)]);
}
