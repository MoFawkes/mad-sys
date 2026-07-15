namespace AqiClock.Domain.Entities;

/// <summary>A signed-in user's server-side profile. The role here is display/UI-gating only; authority lives in RLS.</summary>
public sealed record Profile(
    Guid Id,
    string DisplayName,
    UserRole Role,
    bool IsActive);
