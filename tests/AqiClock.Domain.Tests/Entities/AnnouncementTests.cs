using AqiClock.Domain.Entities;

namespace AqiClock.Domain.Tests.Entities;

public sealed class AnnouncementTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    private static Announcement WithExpiry(DateTimeOffset? expiresAt) =>
        new(Guid.NewGuid(), "Title", "Body", Now.AddHours(-1), Guid.NewGuid(), expiresAt);

    [Fact]
    public void NullExpiryNeverExpires() =>
        Assert.False(WithExpiry(null).IsExpiredAt(DateTimeOffset.MaxValue));

    [Fact]
    public void FutureExpiryIsNotExpired() =>
        Assert.False(WithExpiry(Now.AddMinutes(1)).IsExpiredAt(Now));

    [Fact]
    public void PastExpiryIsExpired() =>
        Assert.True(WithExpiry(Now.AddMinutes(-1)).IsExpiredAt(Now));

    [Fact]
    public void ExactExpiryInstantCountsAsExpired() =>
        Assert.True(WithExpiry(Now).IsExpiredAt(Now));
}
