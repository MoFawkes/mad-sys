using AqiClock.Domain.Time;

namespace AqiClock.Application.Tests;

// Institute wall time deliberately differs from device time in both directions.
internal sealed class AnnouncementTestClock(int offsetHours) : IInstituteClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    public DateTime DeviceNow => UtcNow.LocalDateTime;
    public DateTime Now => DateTime.SpecifyKind(DeviceNow.AddHours(offsetHours), DateTimeKind.Unspecified);
    public DateOnly LocalToday => DateOnly.FromDateTime(Now);
    public string TimeZoneId => "Test institute zone";
    public bool DiffersFromDeviceZone => true;
}
