namespace AqiClock.Domain.Entities;

/// <summary>An admin broadcast shown to all staff. A null expiry means "until deleted".</summary>
public sealed record Announcement(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? ExpiresAt)
{
    public bool IsExpiredAt(DateTimeOffset instant) => ExpiresAt is { } expiry && expiry <= instant;
}
