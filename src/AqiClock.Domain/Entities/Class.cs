namespace AqiClock.Domain.Entities;

#pragma warning disable CA1716 // Domain language intentionally calls this entity Class.
public sealed record Class(Guid Id, string Name, int SortOrder);
#pragma warning restore CA1716

public sealed record PeriodClass(Guid PeriodId, Guid ClassId);
