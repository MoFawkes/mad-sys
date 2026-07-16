namespace AqiClock.Application.Sync;

public sealed class BackoffPolicy
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(5);

    public static TimeSpan GetDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        double seconds = Math.Min(30 * Math.Pow(2, consecutiveFailures - 1), MaximumDelay.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}

public sealed class DebouncePolicy
{
    public DebouncePolicy(TimeSpan delay) => Delay = delay >= TimeSpan.Zero ? delay : throw new ArgumentOutOfRangeException(nameof(delay));

    public TimeSpan Delay { get; }
}
