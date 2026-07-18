namespace AqiClock.Application.Sync;

public sealed class BackoffPolicy
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(5);

    public static TimeSpan GetDelay(int consecutiveFailures) =>
        GetDelay(consecutiveFailures, TimeSpan.FromSeconds(30), MaximumDelay);

    public static TimeSpan GetDelay(int consecutiveFailures, TimeSpan initialDelay, TimeSpan maximumDelay)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDelay, initialDelay);

        double seconds = Math.Min(
            initialDelay.TotalSeconds * Math.Pow(2, consecutiveFailures - 1),
            maximumDelay.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}

public sealed class DebouncePolicy
{
    public DebouncePolicy(TimeSpan delay) => Delay = delay >= TimeSpan.Zero ? delay : throw new ArgumentOutOfRangeException(nameof(delay));

    public TimeSpan Delay { get; }
}
