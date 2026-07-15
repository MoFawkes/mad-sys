namespace AqiClock.Domain.Time;

/// <summary>
/// Wall-clock time source. All schedule computation is local wall-clock by design
/// (docs/DECISIONS.md ADR-006); injecting this keeps the engine fully testable.
/// </summary>
public interface IClock
{
    /// <summary>Current local time.</summary>
    DateTime Now { get; }

    /// <summary>Current local calendar date.</summary>
    DateOnly LocalToday { get; }
}
