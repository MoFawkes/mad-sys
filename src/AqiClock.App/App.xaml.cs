using System.Globalization;
using System.IO;
using System.Windows;
using AqiClock.Infrastructure.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AqiClock.App;

public partial class App : System.Windows.Application
{
    private static readonly Version? ApplicationVersion = typeof(App).Assembly.GetName().Version;

    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables(prefix: "AQICLOCK_")
            .Build();

        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AqiClock",
            "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDirectory, "aqiclock-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging(builder => builder
            .AddConfiguration(configuration.GetSection("Logging"))
            .AddSerilog(dispose: true)
            .AddDebug());
        services.AddAqiClockInfrastructure(configuration);
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        ILogger<App> logger = _serviceProvider.GetRequiredService<ILogger<App>>();
        if (logger.IsEnabled(LogLevel.Information))
        {
            LogStarting(logger, ApplicationVersion);
        }

        _serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AQI Clock starting (version {Version})")]
    private static partial void LogStarting(Microsoft.Extensions.Logging.ILogger logger, Version? version);
}
