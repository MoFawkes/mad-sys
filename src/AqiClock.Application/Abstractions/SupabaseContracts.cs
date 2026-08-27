namespace AqiClock.Application.Abstractions;

using System.Text.Json.Nodes;

public sealed record TableChangeSignal(CacheTable Table);

public interface IRealtimeSubscription : IAsyncDisposable
{
    bool IsAlive => true;
    event EventHandler? Closed { add { } remove { } }
}

public interface ISupabaseGateway
{
    Task<AuthenticatedSession> SignInAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<AuthenticatedSession> SignInAnonymouslyAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<AuthenticatedSession>(new NotSupportedException());
    Task<Guid> EnrollStudentDeviceAsync(string joinCode, CancellationToken cancellationToken = default) =>
        Task.FromException<Guid>(new NotSupportedException());
    Task SendPasswordResetAsync(string email, CancellationToken cancellationToken = default);
    Task CompletePasswordRecoveryAsync(string accessToken, string newPassword, CancellationToken cancellationToken = default);
    Task<AuthenticatedSession> RefreshSessionAsync(StoredSession session, CancellationToken cancellationToken = default);
    Task RestoreAccessTokenAsync(StoredSession session, CancellationToken cancellationToken = default) => Task.CompletedTask;
    Task SignOutAsync(CancellationToken cancellationToken = default);
    Task<Guid> GetCurrentOrganizationIdAsync(CancellationToken cancellationToken = default);
    Task<DateOnly> GetCurrentOrganizationDateAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<DateOnly>(new NotSupportedException());
    Task<CacheSnapshot> PullAsync(CacheTable table, CancellationToken cancellationToken = default);
    Task InsertAsync(CacheTable table, object row, CancellationToken cancellationToken = default);
    Task UpdateAsync(CacheTable table, Guid id, object row, CancellationToken cancellationToken = default);
    Task DeleteAsync(CacheTable table, Guid id, CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(Guid id, string? role, bool? isActive, CancellationToken cancellationToken = default);
    Task SaveTimetableAsync(TimetableRow timetable, IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default);
    Task SaveWeekScheduleRowAsync(int weekday, Guid? audienceClassId, Guid? timetableId, CancellationToken cancellationToken = default);
    Task DeleteWeekScheduleRowAsync(int weekday, Guid audienceClassId, CancellationToken cancellationToken = default);
    [Obsolete("Compatibility only; use SaveWeekScheduleRowAsync.")]
    Task UpdateWeekScheduleAsync(int weekday, Guid? timetableId, CancellationToken cancellationToken = default) => SaveWeekScheduleRowAsync(weekday, null, timetableId, cancellationToken);
    Task<string> GetStudentJoinCodeAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new NotSupportedException());
    Task<string> RotateStudentJoinCodeAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new NotSupportedException());
    Task<int> RevokeStudentDevicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<int>(new NotSupportedException());
    Task<GeneratorAuthoringSnapshot> GetGeneratorAuthoringAsync(Guid timetableId, CancellationToken cancellationToken = default) =>
        Task.FromException<GeneratorAuthoringSnapshot>(new NotSupportedException());
    Task<AnchorConfigurationSnapshot> GetAnchorConfigurationAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<AnchorConfigurationSnapshot>(new NotSupportedException());
    Task<GeneratorMaintenanceRun> RegenerateGeneratedTimetablesAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<GeneratorMaintenanceRun>(new NotSupportedException());
    Task<GeneratorMaintenanceRun?> GetLatestGeneratorMaintenanceRunAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<GeneratorMaintenanceRun?>(null);
    Task SaveGeneratedTimetableAsync(Guid timetableId, GeneratorDefinitionWrite definition,
        IReadOnlyList<GeneratorBlockWrite> blocks, IReadOnlyList<Guid> anchorIds,
        IReadOnlyList<PeriodRow> periods, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException());
    Task<GeneratorServerPreview> PreviewGeneratedTimetableAsync(Guid timetableId,
        GeneratorDefinitionWrite definition, IReadOnlyList<GeneratorBlockWrite> blocks,
        IReadOnlyList<Guid> anchorIds, CancellationToken cancellationToken = default) =>
        Task.FromException<GeneratorServerPreview>(new NotSupportedException());
    Task<int> BulkUpsertAnchorDateOverridesAsync(Guid anchorId,
        IReadOnlyList<AnchorDateOverrideWrite> rows, CancellationToken cancellationToken = default) =>
        Task.FromException<int>(new NotSupportedException());
    Task SaveAnchorStandingTimeAsync(AnchorStandingTime row, bool isNew, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException());
    Task SaveAnchorDateOverrideAsync(AnchorDateOverride row, bool isNew, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException());
    Task<IReadOnlyList<AuditEntry>> GetAuditEntriesAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<IRealtimeSubscription> SubscribeAsync(Func<TableChangeSignal, CancellationToken, Task> onChange, CancellationToken cancellationToken = default);
}

public sealed record AuditEntry(long Id, Guid? ActorId, string Action, string EntityType, Guid EntityId, JsonObject? Before, JsonObject? After, DateTimeOffset CreatedAt);
public sealed record TimetableGeneratorDefinition(Guid TimetableId, Guid OrgId, string SessionKind, TimeOnly DayStart, TimeOnly? AdvisoryDayEnd, string NamingPattern);
public sealed record TimetableGeneratorBlock(Guid Id, Guid TimetableId, Guid OrgId, int SortOrder, string BlockKind, string? Name, int? LessonCount, int? LessonMinutes, int? BreakMinutes, bool HostsNaseehah);
public sealed record TimetableGeneratorAnchor(Guid TimetableId, Guid AnchorId, Guid OrgId);
public sealed record OrganizationAnchor(Guid Id, Guid OrgId, string Key, string Name, int SortOrder);
public sealed record AnchorStandingTime(Guid Id, Guid OrgId, Guid AnchorId, int? Weekday, TimeOnly StartTime, int? DurationMinutes, DateOnly EffectiveFrom);
public sealed record AnchorDateOverride(Guid Id, Guid OrgId, Guid AnchorId, DateOnly Date, TimeOnly? StartTime, int? DurationMinutes, bool IsCancelled);
public sealed record GeneratorMaintenanceRun(Guid Id, Guid OrgId, DateTimeOffset StartedAt, long DurationMs, DateOnly RegeneratedDate, int TimetablesWritten, string? Error);
public sealed record GeneratorAuthoringSnapshot(TimetableGeneratorDefinition? Definition, IReadOnlyList<TimetableGeneratorBlock> Blocks, IReadOnlyList<TimetableGeneratorAnchor> Anchors);
public sealed record AnchorConfigurationSnapshot(IReadOnlyList<OrganizationAnchor> Anchors, IReadOnlyList<AnchorStandingTime> StandingTimes, IReadOnlyList<AnchorDateOverride> DateOverrides);
public sealed record GeneratorDefinitionWrite(string SessionKind, TimeOnly DayStart, TimeOnly? AdvisoryDayEnd, string NamingPattern);
public sealed record GeneratorBlockWrite(Guid Id, int SortOrder, string BlockKind, string? Name, int? LessonCount, int? LessonMinutes, int? BreakMinutes, bool HostsNaseehah);
public sealed record AnchorDateOverrideWrite(DateOnly Date, TimeOnly? StartTime, int? DurationMinutes, bool IsCancelled = false);
public sealed record GeneratorServerPreview(DateOnly Date, IReadOnlyList<PeriodRow> Periods);

public class ServerWriteException(string message, string? serverCode, Exception? innerException = null) : Exception(message, innerException)
{
    public string? ServerCode { get; } = serverCode;
}
public sealed class ServerDeniedException(string message, string? serverCode = null) : ServerWriteException(message, serverCode);
public sealed class ReferencedRowException(string message) : ServerWriteException(message, "23503");
public sealed class LastAdminException(string message) : ServerWriteException(message, "23514");
public sealed class DuplicateRowException(string message) : ServerWriteException(message, "23505");
