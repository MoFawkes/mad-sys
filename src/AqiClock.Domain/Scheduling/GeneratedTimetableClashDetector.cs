using AqiClock.Domain.Entities;

namespace AqiClock.Domain.Scheduling;

public sealed record GeneratedPeriodClash(Period Left, Period Right, TimeOnly Start, TimeOnly End);

public static class GeneratedTimetableClashDetector
{
    public static IReadOnlyList<GeneratedPeriodClash> Find(IReadOnlyList<Period> left, IReadOnlyList<Period> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var clashes = new List<GeneratedPeriodClash>();
        foreach (Period first in left.Where(period => period.IsValid))
        foreach (Period second in right.Where(period => period.IsValid))
        {
            TimeOnly start = first.StartTime > second.StartTime ? first.StartTime : second.StartTime;
            TimeOnly end = first.EndTime < second.EndTime ? first.EndTime : second.EndTime;
            if (start >= end) continue;
            if (string.Equals(first.Name, second.Name, StringComparison.Ordinal) &&
                first.StartTime == second.StartTime && first.EndTime == second.EndTime) continue;
            clashes.Add(new(first, second, start, end));
        }
        return clashes;
    }
}
