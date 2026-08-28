using AqiClock.Domain.Scheduling;
using System.Globalization;

namespace AqiClock.Domain.Tests;

public sealed class TimetableGeneratorTests
{
    private static readonly Guid TimetableId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public void R6Pm25August2026MatchesEveryMinute()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Pm, new(18, 15),
            [new(Guid.Parse("20000000-0000-0000-0000-000000000001"), GeneratorBlockKind.Lessons, "", 5, 25)],
            [new(Guid.Parse("30000000-0000-0000-0000-000000000001"), "asr", "Asr", new(18, 40), 10),
             new(Guid.Parse("30000000-0000-0000-0000-000000000002"), "maghrib", "Maghrib", new(20, 12), 10)]);

        Assert.Collection(result.Periods,
            p => At(p, "Lesson 1", "18:15", "18:40"),
            p => At(p, "Asr + Naseehah", "18:40", "19:05"),
            p => At(p, "Lesson 2", "19:05", "19:30"),
            p => At(p, "Lesson 3", "19:30", "19:55"),
            p => At(p, "Lesson 4 (part 1)", "19:55", "20:12"),
            p => At(p, "Maghrib", "20:12", "20:22"),
            p => At(p, "Lesson 4 (part 2)", "20:22", "20:30"),
            p => At(p, "Lesson 5", "20:30", "20:55"));
    }

    [Fact]
    public void R6AmMondayToThursdayFinishesBeforeZuhr()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Am, new(9, 10),
            [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 4, 30),
             new(Guid.NewGuid(), GeneratorBlockKind.Break, "Break / Naseehah", 1, 25, true),
             new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 4, 30)],
            [new(Guid.NewGuid(), "zuhr", "Zuhr", new(13, 37), 10)]);

        Assert.Equal(9, result.Periods.Count);
        At(result.Periods[3], "Lesson 4", "10:40", "11:10");
        At(result.Periods[4], "Break / Naseehah", "11:10", "11:35");
        At(result.Periods[8], "Lesson 8", "13:05", "13:35");
        Assert.DoesNotContain(result.Periods, period => period.Name == "Zuhr");
    }

    [Fact]
    public void MissingApplicableAnchorDurationRefusesExpansion()
    {
        Assert.Throws<InvalidOperationException>(() => AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Am, new(9, 10),
            [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 8, 30)],
            [new(Guid.NewGuid(), "zuhr", "Zuhr", new(12, 58), null)]));
    }

    [Fact]
    public void LateIshaAppliesAgainstBumpedSessionEnd()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Pm, new(18, 15),
            [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 5, 25)],
            [new(Guid.NewGuid(), "asr", "Asr", new(18, 40), 10),
             new(Guid.NewGuid(), "maghrib", "Maghrib", new(19, 30), 10),
             new(Guid.NewGuid(), "isha", "Isha", new(20, 30), 10)]);

        GeneratedPeriod isha = Assert.Single(result.Periods, period => period.Name == "Isha");
        At(isha, "Isha", "20:30", "20:40");
        Assert.Equal(new TimeOnly(21, 5), result.Periods[^1].End);
    }

    [Fact]
    public void FridayUsesResolvedRowAndSplitsLessonSevenWithoutFridayLogic()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Am, new(9, 10),
            [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 4, 30),
             new(Guid.NewGuid(), GeneratorBlockKind.Break, "Break", 1, 25, true),
             new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 4, 30)],
            [new(Guid.NewGuid(), "zuhr", "Zuhr", new(12, 58), 30)]);

        At(result.Periods[7], "Lesson 7 (part 1)", "12:35", "12:58");
        At(result.Periods[8], "Zuhr", "12:58", "13:28");
        At(result.Periods[9], "Lesson 7 (part 2)", "13:28", "13:35");
        At(result.Periods[10], "Lesson 8", "13:35", "14:05");
    }

    [Fact]
    public void MovingAnchorPreservesIdsOfUntouchedLessons()
    {
        var block = new GeneratorBlock(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 5, 25);
        Guid anchorId = Guid.NewGuid();
        GeneratorResult first = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Pm, new(18, 15), [block],
            [new(anchorId, "maghrib", "Maghrib", new(19, 32), 10)]);
        GeneratorResult moved = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Pm, new(18, 15), [block],
            [new(anchorId, "maghrib", "Maghrib", new(19, 35), 10)]);

        foreach (string untouched in (string[])["Lesson 1", "Lesson 2", "Lesson 3", "Lesson 5"])
            Assert.Equal(
                Assert.Single(first.Periods, period => period.Name == untouched).Id,
                Assert.Single(moved.Periods, period => period.Name == untouched).Id);
    }

    [Fact]
    public void PmWithoutApplicableAnchorWarnsAndDoesNotInventSlot()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Pm, new(18, 15),
            [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 1, 25)],
            [new(Guid.NewGuid(), "isha", "Isha", new(20, 30), 10)]);
        Assert.Equal("naseehah-unplaced", Assert.Single(result.Warnings).Code);
        Assert.DoesNotContain(result.Periods, period => !period.IsLesson);
    }

    [Fact]
    public void AdvisoryEndOverrunWarnsWithoutShorteningLessons()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Am, new(9, 0),
            [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 2, 30)], [], new(9, 45));
        Assert.Equal(new TimeOnly(10, 0), result.Periods[^1].End);
        Assert.Contains(result.Warnings, warning => warning.Code == "advisory-day-end-overrun");
    }

    [Fact]
    public void LongAnchorPreservesTeachingAcrossTwoOriginalLessonSlots()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Am, new(9, 0),
            [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 3, 30)],
            [new(Guid.NewGuid(), "zuhr", "Zuhr", new(9, 15), 70)]);
        Assert.Equal(90, result.Periods.Where(period => period.IsLesson)
            .Sum(period => (int)(period.End - period.Start).TotalMinutes));
        Assert.Equal(new TimeOnly(11, 40), result.Periods[^1].End);
    }

    [Fact]
    public void NamesAreUniqueAndAuthoredOptionsAffectOutput()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Am, new(9, 0),
            [new(Guid.NewGuid(), GeneratorBlockKind.Break, "Break", 1, 10),
             new(Guid.NewGuid(), GeneratorBlockKind.Break, "Break", 1, 10, true),
             new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 1, 10)], [], namingPattern: "Class {number}");
        Assert.Equal(["Break", "Break / Naseehah", "Class 1"], result.Periods.Select(period => period.Name));
    }

    [Fact]
    public void DuplicatePlainBreakNamesAreDisambiguatedForDatabaseConstraint()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Am, new(9, 0),
            [new(Guid.NewGuid(), GeneratorBlockKind.Break, "Break", 1, 10),
             new(Guid.NewGuid(), GeneratorBlockKind.Break, "Break", 1, 10)], []);
        Assert.Equal(["Break", "Break (2)"], result.Periods.Select(period => period.Name));
    }

    [Fact]
    public void MarginalNaseehahHostInputTerminatesWithBaselineHost()
    {
        GeneratorResult result = AlQalamExpansionRules.Expand(TimetableId, GeneratorSessionKind.Pm, new(18, 15),
            [new(Guid.NewGuid(), GeneratorBlockKind.Lessons, "", 4, 15)],
            [new(Guid.NewGuid(), "x", "X", new(18, 20), 10),
             new(Guid.NewGuid(), "z", "Z", new(19, 35), 10)]);

        Assert.Contains(result.Periods, period => period.Name == "X + Naseehah");
        Assert.Contains(result.Periods, period => period.Name == "Z");
    }

    private static void At(GeneratedPeriod period, string name, string start, string end)
    {
        Assert.Equal(name, period.Name);
        Assert.Equal(TimeOnly.Parse(start, CultureInfo.InvariantCulture), period.Start);
        Assert.Equal(TimeOnly.Parse(end, CultureInfo.InvariantCulture), period.End);
    }
}
