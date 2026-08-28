using System.Security.Cryptography;
using System.Text;

namespace AqiClock.Domain.Scheduling;

public enum GeneratorSessionKind { Am, Pm }
public enum GeneratorBlockKind { Lessons, Break }

public sealed record GeneratorBlock(Guid Id, GeneratorBlockKind Kind, string Name, int Count, int Minutes, bool HostsNaseehah = false);
public sealed record ResolvedAnchor(Guid Id, string Key, string Name, TimeOnly Start, int? DurationMinutes);
public sealed record GeneratedPeriod(Guid Id, string Name, TimeOnly Start, TimeOnly End, bool IsLesson);
public sealed record GeneratorWarning(string Code, string Message);
public sealed record GeneratorResult(IReadOnlyList<GeneratedPeriod> Periods, IReadOnlyList<GeneratorWarning> Warnings);

public static class TimetableGenerator
{
    public static GeneratorResult Expand(
        Guid timetableId,
        GeneratorSessionKind sessionKind,
        TimeOnly dayStart,
        IReadOnlyList<GeneratorBlock> blocks,
        IReadOnlyList<ResolvedAnchor> anchors,
        TimeOnly? advisoryDayEnd = null,
        int prayerMinutes = 10,
        int naseehahMinutes = 15,
        string namingPattern = "Lesson {number}")
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(prayerMinutes);
        ArgumentOutOfRangeException.ThrowIfNegative(naseehahMinutes);

        List<GeneratedPeriod> authored = LayOut(timetableId, dayStart, blocks, namingPattern);
        ResolvedAnchor[] candidates = anchors
            .Where(anchor => anchor.Start >= dayStart)
            .OrderBy(anchor => anchor.Start)
            .ThenBy(anchor => anchor.Id)
            .ToArray();
        var warnings = new List<GeneratorWarning>();

        // Pass one establishes which prayers happen without Naseehah extending the day.
        // Only that stable set participates in host selection. Pass two books Naseehah
        // and may consequently place later anchors, but they cannot retroactively become
        // the host. This makes expansion deterministic for every legal admin-authored row.
        (_, IReadOnlyList<ResolvedAnchor> baselineApplied) =
            ApplyAnchors(timetableId, authored, candidates, null, prayerMinutes, naseehahMinutes);
        ResolvedAnchor? naseehahAnchor = sessionKind == GeneratorSessionKind.Pm
            ? baselineApplied.OrderBy(anchor => DistanceFromSeven(anchor.Start)).ThenBy(anchor => anchor.Start).FirstOrDefault()
            : null;
        (List<GeneratedPeriod> periods, _) =
            ApplyAnchors(timetableId, authored, candidates, naseehahAnchor, prayerMinutes, naseehahMinutes);
        if (sessionKind == GeneratorSessionKind.Pm && naseehahAnchor is null)
            warnings.Add(new("naseehah-unplaced", "No anchor falls within the PM session; Naseehah was not placed."));

        if (advisoryDayEnd is { } softEnd && periods.Count > 0 && periods[^1].End > softEnd)
            warnings.Add(new("advisory-day-end-overrun", $"The generated session ends at {periods[^1].End:HH:mm}, after the advisory end {softEnd:HH:mm}."));
        return new(periods, warnings);
    }

    private static (List<GeneratedPeriod> Periods, IReadOnlyList<ResolvedAnchor> Applied) ApplyAnchors(
        Guid timetableId, List<GeneratedPeriod> authored, IReadOnlyList<ResolvedAnchor> candidates,
        ResolvedAnchor? naseehahAnchor, int prayerMinutes, int naseehahMinutes)
    {
        var periods = authored.ToList();
        var applied = new List<ResolvedAnchor>();
        foreach (ResolvedAnchor anchor in candidates)
        {
            if (periods.Count == 0 || anchor.Start >= periods[^1].End) continue;
            int duration = anchor.DurationMinutes
                ?? throw new InvalidOperationException($"Anchor '{anchor.Name}' has no duration for this date.");
            bool hostsNaseehah = anchor == naseehahAnchor;
            if (hostsNaseehah) duration = prayerMinutes + naseehahMinutes;
            InsertAnchor(timetableId, periods,
                hostsNaseehah ? anchor with { Name = anchor.Name + " + Naseehah" } : anchor, duration);
            applied.Add(anchor);
        }
        return (periods, applied);
    }

    private static List<GeneratedPeriod> LayOut(Guid timetableId, TimeOnly start, IReadOnlyList<GeneratorBlock> blocks, string namingPattern)
    {
        var result = new List<GeneratedPeriod>();
        var names = new List<string>();
        TimeOnly cursor = start;
        int lessonNumber = 0;
        foreach (GeneratorBlock block in blocks)
        {
            if (block.Minutes <= 0 || (block.Kind == GeneratorBlockKind.Lessons && block.Count <= 0))
                throw new ArgumentException("Block counts and durations must be positive.", nameof(blocks));
            int count = block.Kind == GeneratorBlockKind.Lessons ? block.Count : 1;
            for (int slot = 0; slot < count; slot++)
            {
                TimeOnly end = AddMinutes(cursor, block.Minutes);
                string requested = block.Kind == GeneratorBlockKind.Lessons
                    ? namingPattern.Replace("{number}", (++lessonNumber).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    : block.HostsNaseehah && !block.Name.Contains("Naseehah", StringComparison.OrdinalIgnoreCase)
                        ? block.Name + " / Naseehah"
                        : block.Name;
                string name = SchedulingValueRules.UniquePeriodName(requested, names);
                names.Add(name);
                result.Add(new(StableId(timetableId, $"block:{block.Id:N}:slot:{slot}"), name, cursor, end, block.Kind == GeneratorBlockKind.Lessons));
                cursor = end;
            }
        }
        return result;
    }

    private static void InsertAnchor(Guid timetableId, List<GeneratedPeriod> periods, ResolvedAnchor anchor, int duration)
    {
        int containing = periods.FindIndex(period => anchor.Start > period.Start && anchor.Start < period.End);
        int insertion = containing >= 0 ? containing : periods.FindIndex(period => period.Start >= anchor.Start);
        if (insertion < 0) return;

        var replacement = new List<GeneratedPeriod>();
        if (containing >= 0)
        {
            GeneratedPeriod source = periods[containing];
            replacement.Add(source with { Name = source.Name + " (part 1)", End = anchor.Start });
            replacement.Add(new(StableId(timetableId, $"anchor:{anchor.Id:N}"), anchor.Name, anchor.Start, AddMinutes(anchor.Start, duration), false));
            replacement.Add(source with {
                Id = StableId(timetableId, $"period:{source.Id:N}:part:2"), Name = source.Name + " (part 2)",
                Start = AddMinutes(anchor.Start, duration), End = AddMinutes(source.End, duration)
            });
            periods.RemoveAt(containing);
            periods.InsertRange(containing, replacement);
            insertion = containing + replacement.Count;
        }
        else
        {
            periods.Insert(insertion, new(StableId(timetableId, $"anchor:{anchor.Id:N}"), anchor.Name, anchor.Start, AddMinutes(anchor.Start, duration), false));
            insertion++;
        }
        for (int index = insertion; index < periods.Count; index++)
            periods[index] = periods[index] with { Start = AddMinutes(periods[index].Start, duration), End = AddMinutes(periods[index].End, duration) };
    }

    private static int DistanceFromSeven(TimeOnly time) => Math.Abs((int)(time.ToTimeSpan() - new TimeOnly(19, 0).ToTimeSpan()).TotalMinutes);
    private static TimeOnly AddMinutes(TimeOnly time, int minutes)
    {
        TimeSpan result = time.ToTimeSpan() + TimeSpan.FromMinutes(minutes);
        if (!SchedulingValueRules.IsMinuteWithinDay(result)) throw new InvalidOperationException("Generated periods cannot cross midnight.");
        return TimeOnly.FromTimeSpan(result);
    }
    private static Guid StableId(Guid timetableId, string identity)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{timetableId:N}:{identity}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
