namespace AqiClock.Domain.Entities;

public enum AudienceType { Everyone, Teachers, Graduates, Am, Pm, SpecificClass }
public enum UpdateType { General, ClassStarts, Naseehah, MonthlyProgramme, YearlyProgramme }
public enum AnnouncementStatus { Draft, Scheduled, Published }

/// <summary>An audience-aware admin broadcast. Deleted announcements remain available to admin history.</summary>
public sealed record Announcement(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset? ExpiresAt,
    AudienceType AudienceType = AudienceType.Everyone,
    Guid? AudienceClassId = null,
    UpdateType UpdateType = UpdateType.General,
    DateTimeOffset? PublishAt = null,
    string? EMasjidLink = null,
    AnnouncementStatus Status = AnnouncementStatus.Published,
    DateTimeOffset? DeletedAt = null)
{
    public bool IsExpiredAt(DateTimeOffset instant) => ExpiresAt is { } expiry && expiry <= instant;
    public bool IsPublishedAt(DateTimeOffset instant) =>
        DeletedAt is null && Status != AnnouncementStatus.Draft && (PublishAt is null || PublishAt <= instant);
    public bool CanPublishFromHistory => DeletedAt is null;
}
