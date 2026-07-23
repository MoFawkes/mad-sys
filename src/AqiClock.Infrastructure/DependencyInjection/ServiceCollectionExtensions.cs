using AqiClock.Application.Configuration;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Services;
using AqiClock.Application.Sync;
using AqiClock.Domain.Time;
using AqiClock.Infrastructure.Auth;
using AqiClock.Infrastructure.Cache;
using AqiClock.Infrastructure.Supabase;
using AqiClock.Infrastructure.Time;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqiClock.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAqiClockInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddLogging();

        services.AddOptions<AqiClockOptions>()
            .Bind(configuration.GetSection(AqiClockOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SupabaseOptions>()
            .Bind(configuration.GetSection(SupabaseOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton(new DebouncePolicy(TimeSpan.FromMilliseconds(500)));

        services.AddSingleton<SqliteCacheDatabase>();
        services.AddSingleton<ILocalCache>(static provider => provider.GetRequiredService<SqliteCacheDatabase>());
        services.AddSingleton<ITimetableRepository, SqliteTimetableRepository>();
        services.AddSingleton<IAnnouncementRepository, SqliteAnnouncementRepository>();
        services.AddSingleton<IClassRepository, SqliteClassRepository>();
        services.AddSingleton<IDeviceAudienceContext, DeviceAudienceContext>();
        services.AddSingleton<IWeekScheduleRepository, SqliteWeekScheduleRepository>();
        services.AddSingleton<IDateOverrideRepository, SqliteDateOverrideRepository>();
        services.AddSingleton<IProfileRepository, SqliteProfileRepository>();
        services.AddSingleton<INotificationLogStore, SqliteNotificationLogStore>();
        services.AddSingleton<IAnnouncementReadStore, SqliteAnnouncementReadStore>();
        services.AddSingleton<ISessionStore, DpapiSessionStore>();
        services.AddSingleton<ISupabaseGateway, SupabaseGateway>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<ISyncService, SyncService>();

        return services;
    }
}
