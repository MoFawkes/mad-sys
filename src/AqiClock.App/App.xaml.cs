using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics;
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
using Velopack;
using AqiClock.Application.Configuration;

namespace AqiClock.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string InstanceName = @"Local\AqiClock.SingleInstance";
    private const string ActivateName = @"Local\AqiClock.Activate";
    private IHost? _host;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private CancellationTokenSource? _lifetime;
    private bool _ownsMutex;
    private bool _disposed;
    private bool _startMinimized;
    private bool _dispatcherErrorShown;

    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => ProtocolRegistration.Register())
            .OnAfterUpdateFastCallback(_ => ProtocolRegistration.Register())
            .OnBeforeUninstallFastCallback(_ => ProtocolRegistration.Unregister())
            .Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, InstanceName, out bool ownsMutex);
        _ownsMutex = ownsMutex;
        if (!ownsMutex)
        {
            string? recoveryLink = FindRecoveryLink(e.Args);
            bool sent = recoveryLink is not null &&
                ActivationPipe.TrySendAsync(recoveryLink).GetAwaiter().GetResult();
            if (!sent)
            {
                try { EventWaitHandle.OpenExisting(ActivateName).Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
            }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }
        base.OnStartup(e);
        _startMinimized = e.Args.Any(argument => string.Equals(argument, "--minimized", StringComparison.OrdinalIgnoreCase));
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        _host = BuildHost();
        _host.Start();
        _lifetime = new CancellationTokenSource();
        StartActivationListener(_lifetime.Token);
        StartActivationPipeListener(_lifetime.Token);
        StartApplication();
        string? startupRecoveryLink = FindRecoveryLink(e.Args);
        if (startupRecoveryLink is not null) HandleRecoveryLink(startupRecoveryLink);
    }

    private static IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), false).AddEnvironmentVariables("AQICLOCK_");
        string logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqiClock", "logs");
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.File(Path.Combine(logs, "aqiclock-.log"), formatProvider: CultureInfo.InvariantCulture, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7).CreateLogger();
#if DEBUG
        PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingErrorTraceListener());
#endif
        builder.Logging.ClearProviders(); builder.Logging.AddSerilog(Log.Logger, true);
        builder.Services.AddAqiClockInfrastructure(builder.Configuration);
        builder.Services.AddOptions<AqiClockUpdateOptions>().Bind(builder.Configuration.GetSection(AqiClockUpdateOptions.SectionName));
        builder.Services.AddSingleton<ISettingsService, SettingsService>(); builder.Services.AddSingleton<IClockService, ClockService>(); builder.Services.AddSingleton<IWindowService, WindowService>(); builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<INotificationPresenter, ToastPresenter>(); builder.Services.AddSingleton<INotificationScheduler, NotificationScheduler>(); builder.Services.AddSingleton<TrayService>(); builder.Services.AddSingleton<StartupService>(); builder.Services.AddSingleton<IUpdateService, UpdateService>(); builder.Services.AddSingleton<UpdateRestartPrompt>();
        builder.Services.AddSingleton<ClockViewModel>(); builder.Services.AddSingleton<AnnouncementsViewModel>(); builder.Services.AddSingleton<MainViewModel>(); builder.Services.AddTransient<SignInViewModel>(); builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<PasswordRecoveryViewModel>();
        builder.Services.AddTransient<StudentClassPickerViewModel>();
        builder.Services.AddSingleton<TimetableEditorViewModel>(); builder.Services.AddSingleton<WeekScheduleViewModel>(); builder.Services.AddSingleton<OverridesViewModel>(); builder.Services.AddSingleton<ClassesViewModel>(); builder.Services.AddSingleton<AnnouncementComposeViewModel>(); builder.Services.AddSingleton<AuditViewModel>(); builder.Services.AddSingleton<UsersViewModel>(); builder.Services.AddSingleton<StudentDevicesViewModel>(); builder.Services.AddSingleton<AdminViewModel>();
        builder.Services.AddSingleton<MainWindow>(); builder.Services.AddTransient<SignInWindow>(); builder.Services.AddTransient<PasswordRecoveryWindow>(); builder.Services.AddTransient<SettingsWindow>(); builder.Services.AddTransient<AdminWindow>();
        builder.Services.AddTransient<RoleChoiceWindow>(); builder.Services.AddTransient<StudentClassPickerWindow>();
        return builder.Build();
    }

    private void StartApplication()
    {
        IServiceProvider services = _host!.Services;
        ILogger<App> logger = services.GetRequiredService<ILogger<App>>();
        ISettingsService settings = services.GetRequiredService<ISettingsService>();
        StartupStepRunner.TryRun("settings restore", () => settings.LoadAsync().GetAwaiter().GetResult(), logger);
        ThemeService theme = services.GetRequiredService<ThemeService>();
        StartupStepRunner.TryRun("theme", () =>
        {
            theme.Apply(settings.Current.Theme);
            settings.Changed += (_, changed) => Dispatcher.Invoke(() => theme.Apply(changed.Settings.Theme));
        }, logger);
        IDeviceAudienceContext audience = services.GetRequiredService<IDeviceAudienceContext>();
        StartupStepRunner.TryRun("audience restore", () => audience.RestoreAsync().GetAwaiter().GetResult(), logger);
        ISessionService session = services.GetRequiredService<ISessionService>();
        StartupStepRunner.TryRun("session restore", () => session.RestoreAsync().GetAwaiter().GetResult(), logger);

        // Execute the named plan in order. In particular, the clock, tray, and updater must
        // start before view-model initialization can fail or block on remote data.
        foreach (StartupServiceStep step in StartupStepRunner.ServiceOrder)
        {
            bool succeeded = StartupStepRunner.TryRun(step.ToString(), () => RunServiceStartupStep(step, services, session), logger);
            if (!succeeded && step == StartupServiceStep.Clock) ShowCoreStartupFailure();
        }

        IWindowService windows = services.GetRequiredService<IWindowService>();
        if (session.Current.UserId is not null)
        {
            bool studentReady = audience.Current.Role == DeviceAudienceRole.StudentDevice && audience.Current.SelectedClassIds.Count > 0;
            bool studentNeedsSelection = audience.Current.Role == DeviceAudienceRole.StudentDevice && !studentReady;
            if (studentNeedsSelection)
                TryShowCoreWindow("student class picker", windows.ShowStudentClassPickerWindow, logger);
            else if (WindowLifecycle.ShouldShowMainWindowAtStartup(studentReady, _startMinimized, settings.Current.StartMinimized))
                TryShowCoreWindow("main window", windows.ShowMainWindow, logger);
            StartupStepRunner.TryRun("background sync", () => _ = ObserveStartupSyncAsync(services.GetRequiredService<ISyncService>().StartAsync(), logger), logger);
        }
        else TryShowCoreWindow("role choice window", windows.ShowRoleChoiceWindow, logger);
    }

    private static void RunServiceStartupStep(StartupServiceStep step, IServiceProvider services, ISessionService session)
    {
        switch (step)
        {
            case StartupServiceStep.StartupRegistration: services.GetRequiredService<StartupService>().Start(); break;
            case StartupServiceStep.UpdateRestartPrompt: services.GetRequiredService<UpdateRestartPrompt>().Start(); break;
            case StartupServiceStep.Updater: services.GetRequiredService<IUpdateService>().Start(); break;
            case StartupServiceStep.Tray: services.GetRequiredService<TrayService>().Start(); break;
            case StartupServiceStep.NotificationScheduler: services.GetRequiredService<INotificationScheduler>().StartAsync().GetAwaiter().GetResult(); break;
            case StartupServiceStep.Clock: services.GetRequiredService<IClockService>().Start(); break;
            case StartupServiceStep.MainViewModel:
                if (session.Current.UserId is not null)
                    services.GetRequiredService<MainViewModel>().InitializeAsync().GetAwaiter().GetResult();
                break;
            default: throw new ArgumentOutOfRangeException(nameof(step), step, null);
        }
    }

    private void TryShowCoreWindow(string stepName, Action showWindow, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (!StartupStepRunner.TryRun(stepName, showWindow, logger)) ShowCoreStartupFailure();
    }

    private void ShowCoreStartupFailure()
    {
        if (_dispatcherErrorShown) return;
        _dispatcherErrorShown = true;
        MessageBox.Show("AQI Clock encountered a problem. Details were written to the log.", "AQI Clock", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void StartActivationPipeListener(CancellationToken token)
    {
        _ = Task.Run(() => ActivationPipe.ListenAsync(message =>
        {
            _ = Dispatcher.BeginInvoke(() => HandleRecoveryLink(message));
            return Task.CompletedTask;
        }, token), token);
    }

    private void HandleRecoveryLink(string link)
    {
        if (PasswordRecoveryLink.TryParse(link, out PasswordRecoveryRequest? request) && request is not null)
        {
            _host?.Services.GetRequiredService<IWindowService>().ShowPasswordRecoveryWindow(request);
            return;
        }

        MessageBox.Show(
            "This password recovery link is invalid or has expired. Request a new email from the sign-in window.",
            "AQI Clock",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string? FindRecoveryLink(IEnumerable<string> arguments) =>
        arguments.FirstOrDefault(argument =>
            argument.StartsWith("aqiclock://", StringComparison.OrdinalIgnoreCase));

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime?.Cancel(); _activationEvent?.Set(); _host?.Services.GetService<IClockService>()?.StopClock();
        _host?.Services.GetService<IUpdateService>()?.PrepareUpdateOnExit();
        if (_host is not null) { _host.StopAsync().GetAwaiter().GetResult(); _host.Dispose(); }
        _activationEvent?.Dispose(); _lifetime?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
        _ownsMutex = false;
        Log.CloseAndFlush(); _disposed = true; base.OnExit(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _activationEvent?.Dispose(); _lifetime?.Dispose(); _mutex?.Dispose(); _host?.Dispose(); _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception");
        ShowCoreStartupFailure();
        e.Handled = true;
    }
    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) { if (e.ExceptionObject is Exception exception) Log.Fatal(exception, "Unhandled application exception"); }
}
