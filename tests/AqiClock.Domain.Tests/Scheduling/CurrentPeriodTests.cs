using System.Globalization;
using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using static AqiClock.Domain.Tests.Scheduling.TestData;

namespace AqiClock.Domain.Tests.Scheduling;

public sealed class CurrentPeriodTests
{
    private static PeriodOccurrence? CurrentAt(Timetable timetable, string time)
    {
        EffectiveDay day = ScheduleEngine.ResolveDay(WeekOf(timetable), Monday);
        return ScheduleEngine.FindCurrentPeriod(day, TimeOnly.Parse(time, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BeforeFirstPeriodThereIsNoCurrent() =>
        Assert.Null(CurrentAt(NormalDay(), "08:59"));

    [Fact]
    public void PeriodIsCurrentAtItsExactStart() =>
        Assert.Equal("Period 1", CurrentAt(NormalDay(), "09:00")!.Period.Name);

    [Fact]
    public void PeriodIsCurrentMidway() =>
        Assert.Equal("Period 1", CurrentAt(NormalDay(), "09:30")!.Period.Name);

    [Fact]
    public void PeriodIsNotCurrentAtItsExactEnd()
    {
        // 10:00 ends Period 1 and starts Break: end is exclusive, start is inclusive.
        Assert.Equal("Break", CurrentAt(NormalDay(), "10:00")!.Period.Name);
    }

    [Fact]
    public void AfterLastPeriodThereIsNoCurrent() =>
        Assert.Null(CurrentAt(NormalDay(), "11:20"));

    [Fact]
    public void GapBetweenPeriodsHasNoCurrent()
    {
        Timetable gappy = Tt("Gappy",
            P("Period 1", "09:00", "10:00", sort: 1),
            P("Period 2", "10:30", "11:30", sort: 2));

        Assert.Null(CurrentAt(gappy, "10:15"));
    }

    [Fact]
    public void OverlappingPeriodsLatestStartWins()
    {
        Timetable overlapping = Tt("Overlap",
            P("Long block", "09:00", "12:00", sort: 1),
            P("Intervention", "10:00", "10:30", sort: 2));

        Assert.Equal("Intervention", CurrentAt(overlapping, "10:15")!.Period.Name);
        Assert.Equal("Long block", CurrentAt(overlapping, "11:00")!.Period.Name);
    }

    [Fact]
    public void OverlapWithIdenticalStartLowestSortOrderWins()
    {
        Timetable overlapping = Tt("Parallel",
            P("Stream B", "09:00", "10:00", sort: 2),
            P("Stream A", "09:00", "10:00", sort: 1));

        Assert.Equal("Stream A", CurrentAt(overlapping, "09:30")!.Period.Name);
    }

    [Fact]
    public void ClosedDayHasNoCurrentPeriod()
    {
        ScheduleSnapshot snapshot = WeekOf(NormalDay(),
            new DateOverride(Guid.NewGuid(), Monday, TimetableId: null));
        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.Null(ScheduleEngine.FindCurrentPeriod(day, new TimeOnly(9, 30)));
    }
}
