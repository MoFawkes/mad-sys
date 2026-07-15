using AqiClock.Domain.Entities;

namespace AqiClock.Domain.Scheduling;

/// <summary>A period pinned to the concrete calendar date it occurs on.</summary>
public sealed record PeriodOccurrence(DateOnly Date, Period Period)
{
    /// <summary>Local wall-clock start (DateTimeKind.Unspecified by design — ADR-006).</summary>
    public DateTime StartsAt => Date.ToDateTime(Period.StartTime);

    public DateTime EndsAt => Date.ToDateTime(Period.EndTime);
}
