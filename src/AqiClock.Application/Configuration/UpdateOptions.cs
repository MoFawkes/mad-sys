namespace AqiClock.Application.Configuration;

public sealed class AqiClockUpdateOptions
{
    public const string SectionName = "Updates";

    public string RepositoryUrl { get; init; } = string.Empty;

    public int CheckIntervalHours { get; init; } = 6;
}
