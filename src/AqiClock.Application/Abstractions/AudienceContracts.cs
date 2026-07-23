using AqiClock.Domain.Entities;
using AqiClock.Application.Messages;
using CommunityToolkit.Mvvm.Messaging;

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
    void SetTeacher(UserRole role);
    void SetStudent(IEnumerable<Guid> classIds, IEnumerable<SessionHalfDay> optedHalfDays);
    void Clear();
    bool Matches(Announcement announcement);
    bool MatchesPeriod(IReadOnlySet<Guid> periodClassIds);
}

public sealed class DeviceAudienceContext : IDeviceAudienceContext
{
    private readonly IMessenger _messenger;

    public DeviceAudienceContext(IMessenger messenger) => _messenger = messenger;

    public DeviceAudience Current { get; private set; } =
        new(DeviceAudienceRole.Teacher, new HashSet<Guid>(), new HashSet<SessionHalfDay>());

    public void SetTeacher(UserRole role) => SetCurrent(new(
        role == UserRole.Admin ? DeviceAudienceRole.Admin : DeviceAudienceRole.Teacher,
        new HashSet<Guid>(),
        new HashSet<SessionHalfDay>()));

    public void SetStudent(IEnumerable<Guid> classIds, IEnumerable<SessionHalfDay> optedHalfDays) =>
        SetCurrent(new(DeviceAudienceRole.StudentDevice, classIds.ToHashSet(), optedHalfDays.ToHashSet()));

    public void Clear() => SetCurrent(new(
        DeviceAudienceRole.Teacher,
        new HashSet<Guid>(),
        new HashSet<SessionHalfDay>()));

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
        periodClassIds.Overlaps(Current.SelectedClassIds);
}

public sealed class EmptyClassRepository : IClassRepository
{
    public Task<IReadOnlyList<Class>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Class>>([]);
    public Task<IReadOnlySet<Guid>> GetClassIdsForPeriodAsync(Guid periodId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
