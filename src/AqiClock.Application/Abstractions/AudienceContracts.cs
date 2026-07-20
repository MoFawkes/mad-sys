using AqiClock.Domain.Entities;

namespace AqiClock.Application.Abstractions;

public enum DeviceAudienceRole { Teacher, Admin, StudentDevice }
public enum SessionHalfDay { Am, Pm }

public sealed record DeviceAudience(
    DeviceAudienceRole Role,
    IReadOnlySet<Guid> SelectedClassIds,
    SessionHalfDay? HalfDay);

public interface IDeviceAudienceContext
{
    DeviceAudience Current { get; }
    void SetTeacher(UserRole role);
    void SetStudent(IEnumerable<Guid> classIds, SessionHalfDay halfDay);
    void Clear();
    bool Matches(Announcement announcement);
    bool MatchesPeriod(IReadOnlySet<Guid> periodClassIds, TimeOnly startTime);
}

public sealed class DeviceAudienceContext : IDeviceAudienceContext
{
    public DeviceAudience Current { get; private set; } =
        new(DeviceAudienceRole.Teacher, new HashSet<Guid>(), null);

    public void SetTeacher(UserRole role) => Current = new(
        role == UserRole.Admin ? DeviceAudienceRole.Admin : DeviceAudienceRole.Teacher,
        new HashSet<Guid>(),
        null);

    public void SetStudent(IEnumerable<Guid> classIds, SessionHalfDay halfDay) =>
        Current = new(DeviceAudienceRole.StudentDevice, classIds.ToHashSet(), halfDay);

    public void Clear() => Current = new(DeviceAudienceRole.Teacher, new HashSet<Guid>(), null);

    public bool Matches(Announcement announcement) => announcement.AudienceType switch
    {
        AudienceType.Everyone => true,
        AudienceType.Teachers => Current.Role is DeviceAudienceRole.Teacher or DeviceAudienceRole.Admin,
        AudienceType.Graduates => false,
        AudienceType.Am => Current.HalfDay == SessionHalfDay.Am,
        AudienceType.Pm => Current.HalfDay == SessionHalfDay.Pm,
        AudienceType.SpecificClass => announcement.AudienceClassId is { } id && Current.SelectedClassIds.Contains(id),
        _ => false,
    };

    public bool MatchesPeriod(IReadOnlySet<Guid> periodClassIds, TimeOnly startTime)
    {
        if (Current.Role != DeviceAudienceRole.StudentDevice) return true;
        SessionHalfDay periodHalfDay = startTime < new TimeOnly(12, 0) ? SessionHalfDay.Am : SessionHalfDay.Pm;
        return Current.HalfDay == periodHalfDay && periodClassIds.Overlaps(Current.SelectedClassIds);
    }
}

public sealed class EmptyClassRepository : IClassRepository
{
    public Task<IReadOnlyList<Class>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Class>>([]);
    public Task<IReadOnlySet<Guid>> GetClassIdsForPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
