using AqiClock.Domain.Time;

namespace AqiClock.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;

    public DateOnly LocalToday => DateOnly.FromDateTime(DateTime.Now);
}
