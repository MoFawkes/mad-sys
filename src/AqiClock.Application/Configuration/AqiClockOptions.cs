using System.ComponentModel.DataAnnotations;

namespace AqiClock.Application.Configuration;

public sealed class AqiClockOptions
{
    public const string SectionName = "AqiClock";

    [Range(1, 1440)]
    public int CacheFreshnessMinutes { get; init; } = 60;

    public string? DataDirectory { get; init; }
}
