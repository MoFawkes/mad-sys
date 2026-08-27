namespace AqiClock.Domain.Scheduling;

public static class SchedulingValueRules
{
    public static bool IsMinuteWithinDay(TimeSpan value) =>
        value >= TimeSpan.Zero && value <= new TimeSpan(23, 59, 0);

    public static string UniquePeriodName(string requested, IEnumerable<string> existingNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requested);
        ArgumentNullException.ThrowIfNull(existingNames);
        var names = new HashSet<string>(existingNames.Select(name => name.Trim()), StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(requested)) return requested;
        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{requested} ({suffix})";
            if (!names.Contains(candidate)) return candidate;
        }
    }
}
