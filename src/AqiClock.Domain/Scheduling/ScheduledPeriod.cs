using AqiClock.Domain.Entities;

namespace AqiClock.Domain.Scheduling;

public sealed record ScheduledPeriod(Period Period, Guid? ClassId);
