using AqiClock.Domain.Entities;
using AqiClock.Application.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System.Text.Json;

namespace AqiClock.Application.Abstractions;

public enum DeviceAudienceRole { Teacher, Admin, StudentDevice }
public enum SessionHalfDay { Am, Pm }

public sealed record DeviceAudience(
    DeviceAudienceRole Role,
    IReadOnlySet<Guid> SelectedClassIds,
    IReadOnlySet<SessionHalfDay> OptedHalfDays);

public interface IDeviceAudienceContext
{
    DeviceAudience Current { get; }
    Task RestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    void SetTeacher(UserRole role);
    void SetStudent(IEnumerable<Guid> classIds, IEnumerable<SessionHalfDay> optedHalfDays);
    Task SetStudentAsync(IEnumerable<Guid> classIds, IEnumerable<SessionHalfDay> optedHalfDays, CancellationToken cancellationToken = default) { SetStudent(classIds, optedHalfDays); return Task.CompletedTask; }
    void Clear();
    Task ClearAsync(CancellationToken cancellationToken = default) { Clear(); return Task.CompletedTask; }
    bool Matches(Announcement announcement);
    bool MatchesPeriod(IReadOnlySet<Guid> periodClassIds);
}

public sealed class DeviceAudienceContext : IDeviceAudienceContext
{
    private readonly IMessenger _messenger;
    private readonly ILocalCache? _cache;
    private const string PreferencesKey = "student_preferences";

    public DeviceAudienceContext(IMessenger messenger, ILocalCache? cache = null) { _messenger = messenger; _cache = cache; }

    public DeviceAudience Current { get; private set; } =
        new(DeviceAudienceRole.Teacher, new HashSet<Guid>(), new HashSet<SessionHalfDay>());

    public void SetTeacher(UserRole role) => SetCurrent(new(
        role == UserRole.Admin ? DeviceAudienceRole.Admin : DeviceAudienceRole.Teacher,
        new HashSet<Guid>(),
        new HashSet<SessionHalfDay>()));

    public void SetStudent(IEnumerable<Guid> classIds, IEnumerable<SessionHalfDay> optedHalfDays)
    {
        if (_cache is not null) throw new InvalidOperationException("Persisted student audiences must be set asynchronously.");
        SetCurrent(new(DeviceAudienceRole.StudentDevice, classIds.ToHashSet(), optedHalfDays.ToHashSet()));
    }

    public async Task SetStudentAsync(IEnumerable<Guid> classIds, IEnumerable<SessionHalfDay> optedHalfDays, CancellationToken cancellationToken = default)
    {
        DeviceAudience state = new(DeviceAudienceRole.StudentDevice, classIds.ToHashSet(), optedHalfDays.ToHashSet());
        if (_cache is not null)
        {
            await _cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
            await _cache.SetMetaAsync(PreferencesKey, JsonSerializer.Serialize(new StudentPreferences(state.SelectedClassIds.ToArray(), state.OptedHalfDays.ToArray())), cancellationToken).ConfigureAwait(false);
        }
        SetCurrent(state);
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is null) return;
        await _cache.InitializeAsync(cancellationToken).ConfigureAwait(false);
        string? json = await _cache.GetMetaAsync(PreferencesKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return;
        StudentPreferences? preferences = JsonSerializer.Deserialize<StudentPreferences>(json);
        if (preferences is not null) SetCurrent(new(DeviceAudienceRole.StudentDevice, preferences.SelectedClassIds.ToHashSet(), preferences.OptedHalfDays.ToHashSet()));
    }

    public void Clear() => SetCurrent(new(
        DeviceAudienceRole.Teacher,
        new HashSet<Guid>(),
        new HashSet<SessionHalfDay>()));

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null) await _cache.SetMetaAsync(PreferencesKey, string.Empty, cancellationToken).ConfigureAwait(false);
        Clear();
    }

    private sealed record StudentPreferences(Guid[] SelectedClassIds, SessionHalfDay[] OptedHalfDays);

    private void SetCurrent(DeviceAudience state)
    {
        Current = state;
        _messenger.Send(new AudienceChanged(state));
    }

    public bool Matches(Announcement announcement) => announcement.AudienceType switch
    {
        AudienceType.Everyone => true,
        AudienceType.Teachers => Current.Role is DeviceAudienceRole.Teacher or DeviceAudienceRole.Admin,
        AudienceType.Graduates => false,
        AudienceType.Am => Current.OptedHalfDays.Contains(SessionHalfDay.Am),
        AudienceType.Pm => Current.OptedHalfDays.Contains(SessionHalfDay.Pm),
        AudienceType.SpecificClass => announcement.AudienceClassId is { } id && Current.SelectedClassIds.Contains(id),
        _ => false,
    };

    public bool MatchesPeriod(IReadOnlySet<Guid> periodClassIds) =>
        Current.Role != DeviceAudienceRole.StudentDevice ||
        periodClassIds.Count == 0 ||
        periodClassIds.Overlaps(Current.SelectedClassIds);
}

public sealed class EmptyClassRepository : IClassRepository
{
    public Task<IReadOnlyList<Class>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Class>>([]);
    public Task<IReadOnlySet<Guid>> GetClassIdsForPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
