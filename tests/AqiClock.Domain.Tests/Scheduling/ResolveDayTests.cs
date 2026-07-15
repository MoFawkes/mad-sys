using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;
using static AqiClock.Domain.Tests.Scheduling.TestData;

namespace AqiClock.Domain.Tests.Scheduling;

public sealed class ResolveDayTests
{
    [Fact]
    public void OverrideWinsOverWeekSchedule()
    {
        Timetable normal = NormalDay();
        Timetable exam = Tt("Exam Day", P("Exam", "08:30", "12:00"));
        var snapshot = new ScheduleSnapshot(
            [normal, exam],
            new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Monday] = normal.Id }),
            [new DateOverride(Guid.NewGuid(), Monday, exam.Id)]);

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.Equal(EffectiveDaySource.Override, day.Source);
        Assert.Equal(exam.Id, day.Timetable!.Id);
    }

    [Fact]
    public void ClosedOverrideYieldsNoSchoolEvenWhenWeekdayIsAssigned()
    {
        ScheduleSnapshot snapshot = WeekOf(NormalDay(),
            new DateOverride(Guid.NewGuid(), Monday, TimetableId: null, Note: "Eid holiday"));

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.Equal(EffectiveDaySource.Override, day.Source);
        Assert.Null(day.Timetable);
        Assert.False(day.IsSchoolDay);
        Assert.Empty(day.Periods);
    }

    [Fact]
    public void WeekScheduleAppliesWhenNoOverrideExists()
    {
        Timetable normal = NormalDay();
        ScheduleSnapshot snapshot = WeekOf(normal);

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.Equal(EffectiveDaySource.WeekSchedule, day.Source);
        Assert.Equal(normal.Id, day.Timetable!.Id);
        Assert.True(day.IsSchoolDay);
    }

    [Fact]
    public void UnassignedWeekdayIsNoSchool()
    {
        ScheduleSnapshot snapshot = WeekOf(NormalDay());
        DateOnly saturday = new(2026, 7, 18);

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, saturday);

        Assert.Equal(EffectiveDaySource.None, day.Source);
        Assert.False(day.IsSchoolDay);
    }

    [Fact]
    public void NullWeekdayAssignmentIsNoSchool()
    {
        var snapshot = new ScheduleSnapshot(
            [NormalDay()],
            new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Monday] = null }),
            []);

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.Equal(EffectiveDaySource.None, day.Source);
        Assert.False(day.IsSchoolDay);
    }

    [Fact]
    public void OverrideReferencingMissingTimetableIsTreatedAsClosed()
    {
        ScheduleSnapshot snapshot = WeekOf(NormalDay(),
            new DateOverride(Guid.NewGuid(), Monday, Guid.NewGuid()));

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.Equal(EffectiveDaySource.Override, day.Source);
        Assert.Null(day.Timetable);
        Assert.False(day.IsSchoolDay);
    }

    [Fact]
    public void WeekScheduleReferencingMissingTimetableIsNoSchoolDay()
    {
        var snapshot = new ScheduleSnapshot(
            [],
            new WeekSchedule(new Dictionary<DayOfWeek, Guid?> { [DayOfWeek.Monday] = Guid.NewGuid() }),
            []);

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.Equal(EffectiveDaySource.WeekSchedule, day.Source);
        Assert.Null(day.Timetable);
        Assert.False(day.IsSchoolDay);
    }

    [Fact]
    public void InvalidPeriodsAreExcludedFromTheEffectiveDay()
    {
        Timetable timetable = Tt("Bad Data Day",
            P("Fine", "09:00", "10:00", sort: 1),
            P("Inverted", "11:00", "10:30", sort: 2),
            P("Zero length", "12:00", "12:00", sort: 3));
        ScheduleSnapshot snapshot = WeekOf(timetable);

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.Single(day.Periods);
        Assert.Equal("Fine", day.Periods[0].Name);
    }

    [Fact]
    public void PeriodsAreSortedByStartTimeThenSortOrder()
    {
        Timetable timetable = Tt("Unordered",
            P("C", "11:00", "12:00", sort: 1),
            P("B2", "09:00", "09:45", sort: 5),
            P("B1", "09:00", "10:00", sort: 2),
            P("A", "08:00", "09:00", sort: 9));
        ScheduleSnapshot snapshot = WeekOf(timetable);

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        string[] expected = ["A", "B1", "B2", "C"];
        Assert.Equal(expected, day.Periods.Select(p => p.Name));
    }

    [Fact]
    public void ArchivedTimetableStillResolvesWhenReferenced()
    {
        // Archiving only hides a timetable from admin pickers; existing assignments keep working.
        var archived = new Timetable(Guid.NewGuid(), "Old Day", IsArchived: true,
            [P("Period 1", "09:00", "10:00")]);
        ScheduleSnapshot snapshot = WeekOf(archived);

        EffectiveDay day = ScheduleEngine.ResolveDay(snapshot, Monday);

        Assert.True(day.IsSchoolDay);
    }

    [Fact]
    public void EmptySnapshotResolvesEveryDayAsNoSchool()
    {
        EffectiveDay day = ScheduleEngine.ResolveDay(ScheduleSnapshot.Empty, Monday);

        Assert.Equal(EffectiveDaySource.None, day.Source);
        Assert.False(day.IsSchoolDay);
    }
}
