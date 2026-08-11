using AqiClock.Application.Abstractions;
using AqiClock.Application.Messages;
using AqiClock.Domain.Time;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;

namespace AqiClock.Infrastructure.Time;

public sealed partial class InstituteClock : IInstituteClock, IRecipient<DataChanged>
{
    private readonly IOrganizationRepository _organizations;
    private readonly ILogger<InstituteClock> _logger;
    private readonly object _gate = new();
    private TimeZoneInfo? _zone;
    private string _timeZoneId = TimeZoneInfo.Local.Id;
    private bool _loaded;

    public InstituteClock(IOrganizationRepository organizations, IMessenger messenger, ILogger<InstituteClock> logger)
    { _organizations = organizations; _logger = logger; messenger.Register(this); }

    public DateTime Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zone).DateTime;
    public DateOnly LocalToday => DateOnly.FromDateTime(Now);
    public DateTime DeviceNow => DateTime.Now;
    public string TimeZoneId { get { _ = Zone; return _timeZoneId; } }
    public bool DiffersFromDeviceZone => Zone.Id != TimeZoneInfo.Local.Id && !Zone.HasSameRules(TimeZoneInfo.Local);

    public void Receive(DataChanged message)
    { if (message.Table == CacheTable.Organizations) lock (_gate) { _loaded = false; _zone = null; } }

    private TimeZoneInfo Zone
    {
        get
        {
            lock (_gate)
            {
                if (_loaded) return _zone!;
                _loaded = true;
                try
                {
                    OrganizationInfo? organization = _organizations.GetAsync().GetAwaiter().GetResult();
                    if (organization is null) throw new TimeZoneNotFoundException("Organization cache is empty.");
                    _timeZoneId = organization.TimeZone;
                    _zone = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
                }
                catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    _timeZoneId = TimeZoneInfo.Local.Id;
                    _zone = TimeZoneInfo.Local;
                    LogFallback(_logger, exception, _timeZoneId);
                }
                return _zone;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Institute timezone unavailable; using device timezone {TimeZoneId}")]
    private static partial void LogFallback(ILogger logger, Exception exception, string timeZoneId);
}
