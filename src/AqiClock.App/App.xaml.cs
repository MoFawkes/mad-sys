using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AqiClock.App.Services;
using AqiClock.App.ViewModels;
using AqiClock.App.Views;
using AqiClock.Application.Abstractions;
using AqiClock.Application.Services;
using AqiClock.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace AqiClock.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string InstanceName = @"Local\AqiClock.SingleInstance";
    private const string ActivateName = @"Local\AqiClock.Activate";
    private IHost? _host;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _lifetime;
    private bool _disposed;
    private bool _startMinimized;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, InstanceName, out bool ownsMutex);
        if (!ownsMutex) { try { EventWaitHandle.OpenExisting(ActivateName).Set(); } catch (WaitHandleCannotBeOpenedException) { } Shutdown(); return; }
        base.OnStartup(e);
        _startMinimized = e.Args.Any(argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        _host = BuildHost();
        _host.Start();
        _lifetime = new CancellationTokenSource();
        StartActivationListener(_lifetime.Token);
        StartApplication();
    }

    private static IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), false).AddEnvironmentVariables("AQICLOCK_");
        string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqiClock", "logs");
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.File(Path.Combine(logs, "aqiclock-.log"), formatProvider: CultureInfo.InvariantCulture, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7).CreateLogger();
        builder.Logging.ClearProviders(); builder.Logging.AddSerilog(Log.Logger, true);
        builder.Services.AddAqiClockInfrastructure(builder.Configuration);
        builder.Services.AddSingleton<ISettingsService, SettingsService>(); builder.Services.AddSingleton<IClockService, ClockService>(); builder.Services.AddSingleton<IWindowService, WindowService>(); builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<INotificationPresenter, ToastPresenter>(); builder.Services.AddSingleton<INotificationScheduler, NotificationScheduler>(); builder.Services.AddSingleton<TrayService>(); builder.Services.AddSingleton<StartupService>();
        builder.Services.AddSingleton<ClockViewModel>(); builder.Services.AddSingleton<AnnouncementsViewModel>(); builder.Services.AddSingleton<MainViewModel>(); builder.Services.AddTransient<SignInViewModel>(); builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddSingleton<TimetableEditorViewModel>(); builder.Services.AddSingleton<WeekScheduleViewModel>(); builder.Services.AddSingleton<OverridesViewModel>(); builder.Services.AddSingleton<AnnouncementComposeViewModel>(); builder.Services.AddSingleton<AuditViewModel>(); builder.Services.AddSingleton<UsersViewModel>(); builder.Services.AddSingleton<AdminViewModel>();
        builder.Services.AddSingleton<MainWindow>(); builder.Services.AddTransient<SignInWindow>(); builder.Services.AddTransient<SettingsWindow>(); builder.Services.AddTransient<AdminWindow>();
        return builder.Build();
    }

    private void StartApplication()
    {
        IServiceProvider services = _host!.Services;
        ISettingsService settings = services.GetRequiredService<ISettingsService>(); settings.LoadAsync().GetAwaiter().GetResult();
        ThemeService theme = services.GetRequiredService<ThemeService>(); theme.Apply(settings.Current.Theme); settings.Changed += (_, changed) => Dispatcher.Invoke(() => theme.Apply(changed.Settings.Theme));
        ISessionService session = services.GetRequiredService<ISessionService>(); session.RestoreAsync().GetAwaiter().GetResult();
        if (session.Current.UserId is not null) services.GetRequiredService<MainViewModel>().InitializeAsync().GetAwaiter().GetResult();
        services.GetRequiredService<StartupService>().Start();
        services.GetRequiredService<TrayService>().Start();
        services.GetRequiredService<INotificationScheduler>().StartAsync().GetAwaiter().GetResult();
        services.GetRequiredService<IClockService>().Start();
        IWindowService windows = services.GetRequiredService<IWindowService>();
        if (session.Current.UserId is not null)
        {
            if (!_startMinimized && !settings.Current.StartMinimized) windows.ShowMainWindow();
            _ = ObserveStartupSyncAsync(services.GetRequiredService<ISyncService>().StartAsync(), services.GetRequiredService<ILogger<App>>());
        }
        else windows.ShowSignInWindow();
    }

    private static async Task ObserveStartupSyncAsync(Task syncTask, ILogger<App> logger)
    {
        try { await syncTask.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { LogStartupSyncFailed(logger, exception); }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Background sync startup failed after session restore")]
    private static partial void LogStartupSyncFailed(Microsoft.Extensions.Logging.ILogger logger, Exception exception);

    private void StartActivationListener(CancellationToken token)
    {
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateName);
        _ = Task.Run(() => { while (!token.IsCancellationRequested) { _activationEvent.WaitOne(); if (!token.IsCancellationRequested) Dispatcher.Invoke(() => _host?.Services.GetRequiredService<IWindowService>().ActivateMainWindow()); } }, token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime?.Cancel(); _activationEvent?.Set(); _host?.Services.GetService<IClockService>()?.StopClock();
        if (_host is not null) { _host.StopAsync().GetAwaiter().GetResult(); _host.Dispose(); }
        _activationEvent?.Dispose(); _lifetime?.Dispose(); _mutex?.ReleaseMutex(); _mutex?.Dispose(); Log.CloseAndFlush(); _disposed = true; base.OnExit(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _activationEvent?.Dispose(); _lifetime?.Dispose(); _mutex?.Dispose(); _host?.Dispose(); _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) { Log.Error(e.Exception, "Unhandled UI exception"); MessageBox.Show("AQI Clock encountered a problem. Details were written to the log.", "AQI Clock", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; }
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) { if (e.ExceptionObject is Exception exception) Log.Fatal(exception, "Unhandled application exception"); }
}
