using AqiClock.Domain.Entities;
using AqiClock.Domain.Scheduling;

namespace AqiClock.Domain.Tests;

public sealed class GeneratedTimetableClashDetectorTests
{
    [Fact]
    public void IdenticalNamesAndBoundsAreSilentDespiteDifferentIds()
    {
        Period left = new(Guid.NewGuid(), "Maghrib", new(19, 30), new(19, 40), 0, false);
        Period right = left with { Id = Guid.NewGuid() };
        Assert.Empty(GeneratedTimetableClashDetector.Find([left], [right]));
    }

    [Fact]
    public void AnyDifferentlyLabelledOverlapWarnsWithoutDurationFloor()
    {
        Period lesson = new(Guid.NewGuid(), "Lesson 3", new(19, 0), new(19, 30), 0);
        Period prayer = new(Guid.NewGuid(), "Maghrib", new(19, 29), new(19, 40), 0, false);
        GeneratedPeriodClash clash = Assert.Single(GeneratedTimetableClashDetector.Find([lesson], [prayer]));
        Assert.Equal(new TimeOnly(19, 29), clash.Start);
        Assert.Equal(new TimeOnly(19, 30), clash.End);
    }

    [Fact]
    public void SameNameWithDifferentBoundsWarns()
    {
        Period left = new(Guid.NewGuid(), "Lesson 3", new(19, 0), new(19, 30), 0);
        Period right = new(Guid.NewGuid(), "Lesson 3", new(19, 5), new(19, 30), 0);
        Assert.Single(GeneratedTimetableClashDetector.Find([left], [right]));
    }

    [Fact]
    public void TouchingBoundariesDoNotOverlap()
    {
        Period left = new(Guid.NewGuid(), "Lesson 1", new(9, 0), new(9, 30), 0);
        Period right = new(Guid.NewGuid(), "Lesson 2", new(9, 30), new(10, 0), 0);
        Assert.Empty(GeneratedTimetableClashDetector.Find([left], [right]));
    }
}
