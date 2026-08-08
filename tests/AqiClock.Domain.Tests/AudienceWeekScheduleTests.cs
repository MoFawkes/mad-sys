using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;

namespace AqiClock.Domain.Tests;

public sealed class AudienceWeekScheduleTests
{
    private static readonly DateOnly Monday = new(2026, 8, 10);

    [Fact]
    public void TrackBeatsDefaultAndNoMatchUsesDefault()
    {
        Guid classId = Guid.NewGuid(), defaultId = Guid.NewGuid(), trackId = Guid.NewGuid();
        WeekSchedule week = new([
            new(Guid.NewGuid(), DayOfWeek.Monday, null, defaultId),
            new(Guid.NewGuid(), DayOfWeek.Monday, classId, trackId)]);

        Assert.Equal(trackId, week.ResolveFor(DayOfWeek.Monday, new HashSet<Guid> { classId })?.TimetableId);
        Assert.Equal(defaultId, week.ResolveFor(DayOfWeek.Monday, new HashSet<Guid>())?.TimetableId);
    }

    [Fact]
    public void MatchedClosedTrackDoesNotFallThroughToOpenDefault()
    {
        Guid classId = Guid.NewGuid(), defaultId = Guid.NewGuid();
        WeekSchedule week = new([
            new(Guid.NewGuid(), DayOfWeek.Monday, null, defaultId),
            new(Guid.NewGuid(), DayOfWeek.Monday, classId, null)]);
        ScheduleSnapshot snapshot = new([Timetable(defaultId, "Default")], week, [], new HashSet<Guid> { classId });

        Assert.Null(ScheduleEngine.ResolveDay(snapshot, Monday).Timetable);
    }

    [Fact]
    public void MultipleMatchesChooseLowestClassId()
    {
        Guid low = Guid.Parse("7fffffff-0000-0000-0000-000000000001");
        Guid high = Guid.Parse("80000000-0000-0000-0000-000000000002");
        Guid lowTimetable = Guid.NewGuid(), highTimetable = Guid.NewGuid();
        WeekSchedule week = new([
            new(Guid.NewGuid(), DayOfWeek.Monday, high, highTimetable),
            new(Guid.NewGuid(), DayOfWeek.Monday, low, lowTimetable)]);

        Assert.Equal(lowTimetable, week.ResolveFor(DayOfWeek.Monday, new HashSet<Guid> { high, low })?.TimetableId);
    }

    [Fact]
    public void OverrideStillWinsAndNextPeriodKeepsViewerTrack()
    {
        Guid classId = Guid.NewGuid(), defaultId = Guid.NewGuid(), trackId = Guid.NewGuid(), overrideId = Guid.NewGuid();
        WeekSchedule week = new([
            new(Guid.NewGuid(), DayOfWeek.Monday, null, defaultId),
            new(Guid.NewGuid(), DayOfWeek.Monday, classId, trackId)]);
        ScheduleSnapshot snapshot = new(
            [Timetable(defaultId, "Default"), Timetable(trackId, "Track"), Timetable(overrideId, "Override")],
            week, [new DateOverride(Guid.NewGuid(), Monday, overrideId, null)], new HashSet<Guid> { classId });

        Assert.Equal("Override", ScheduleEngine.ResolveDay(snapshot, Monday).Timetable?.Name);
        ScheduleSnapshot withoutOverride = new([Timetable(defaultId, "Default"), Timetable(trackId, "Track")], week, [], new HashSet<Guid> { classId });
        Assert.Equal("Track lesson", ScheduleEngine.FindNextPeriod(withoutOverride, Monday.ToDateTime(new TimeOnly(23, 0)))?.Period.Name);
    }

    private static Timetable Timetable(Guid id, string name) => new(id, name, false,
        [new Period(Guid.NewGuid(), $"{name} lesson", new TimeOnly(9, 0), new TimeOnly(10, 0), 0, true)]);
}
