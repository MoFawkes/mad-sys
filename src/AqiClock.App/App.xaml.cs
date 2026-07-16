using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using AqiClock.App.Services;
using AqiClock.App.ViewModels;
using AqiClock.App.Views;
using AqiClock.Application.Abstractions;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, InstanceName, out bool ownsMutex);
        if (!ownsMutex) { try { EventWaitHandle.OpenExisting(ActivateName).Set(); } catch (WaitHandleCannotBeOpenedException) { } Shutdown(); return; }
        base.OnStartup(e);
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
        builder.Services.AddSingleton<ClockViewModel>(); builder.Services.AddSingleton<AnnouncementsViewModel>(); builder.Services.AddSingleton<MainViewModel>(); builder.Services.AddTransient<SignInViewModel>(); builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddSingleton<MainWindow>(); builder.Services.AddTransient<SignInWindow>(); builder.Services.AddTransient<SettingsWindow>();
        return builder.Build();
    }

    private void StartApplication()
    {
        IServiceProvider services = _host!.Services;
        ISettingsService settings = services.GetRequiredService<ISettingsService>(); settings.LoadAsync().GetAwaiter().GetResult();
        ThemeService theme = services.GetRequiredService<ThemeService>(); theme.Apply(settings.Current.Theme); settings.Changed += (_, changed) => Dispatcher.Invoke(() => theme.Apply(changed.Settings.Theme));
        services.GetRequiredService<IClockService>().Start();
        ISessionService session = services.GetRequiredService<ISessionService>(); session.RestoreAsync().GetAwaiter().GetResult();
        IWindowService windows = services.GetRequiredService<IWindowService>();
        if (session.Current.UserId is not null) { windows.ShowMainWindow(); _ = services.GetRequiredService<ISyncService>().StartAsync(); }
        else windows.ShowSignInWindow();
    }

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
