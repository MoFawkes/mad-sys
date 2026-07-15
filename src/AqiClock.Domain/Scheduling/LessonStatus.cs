namespace AqiClock.Domain.Scheduling;

/// <summary>
/// Everything the clock display needs at one instant: the resolved day, the current
/// period (if any), and the next period (today or on a later school day).
/// </summary>
public sealed record LessonStatus(
    DateTime Timestamp,
    EffectiveDay Day,
    PeriodOccurrence? Current,
    PeriodOccurrence? Next)
{
    /// <summary>Time left in the current period, clamped at zero. Null when no period is active.</summary>
    public TimeSpan? TimeRemaining
    {
        get
        {
            if (Current is null)
            {
                return null;
            }

            TimeSpan remaining = Current.EndsAt - Timestamp;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>Fraction of the current period elapsed, clamped to [0, 1]. Null when no period is active.</summary>
    public double? Progress
    {
        get
        {
            if (Current is null)
            {
                return null;
            }

            double total = (Current.EndsAt - Current.StartsAt).TotalSeconds;
            double elapsed = (Timestamp - Current.StartsAt).TotalSeconds;
            return Math.Clamp(elapsed / total, 0d, 1d);
        }
    }
}
