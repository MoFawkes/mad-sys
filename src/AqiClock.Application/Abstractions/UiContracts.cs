namespace AqiClock.Application.Abstractions;

public enum DisplayMode { Normal, Compact }
public enum AppTheme { System, Light, Dark }

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool IsMaximized = false);

public sealed record AppSettings
{
    public bool StartWithWindows { get; init; } = true;
    public bool StartMinimized { get; init; } = true;
    public bool CloseToTray { get; init; } = true;
    public AppTheme Theme { get; init; } = AppTheme.System;
    public bool AlwaysOnTop { get; init; }
    public bool CompactOnLaunch { get; init; }
    public bool LessonStartNotifications { get; init; } = true;
    public bool EndWarningNotifications { get; init; } = true;
    public int EndWarningMinutes { get; init; } = 5;
    public bool AnnouncementNotifications { get; init; } = true;
    public WindowPlacement? NormalPlacement { get; init; }
    public WindowPlacement? CompactPlacement { get; init; }
}

public sealed record SettingsChanged(AppSettings Settings);

public interface ISettingsService
{
    AppSettings Current { get; }
    event EventHandler<SettingsChanged>? Changed;
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IWindowService
{
    void ShowMainWindow();
    void ShowSignInWindow();
    void ShowSettingsWindow();
    void ShowAdminWindow();
    void CloseAdminWindow(string? reason = null);
    bool Confirm(string message, string title);
    void ShowAnnouncements();
    void HideMainWindow();
    void ActivateMainWindow();
    void CloseSignInWindow();
    void ShutdownApplication();
    void ExitApplication();
}

public interface IClockService
{
    void Start();
    void StopClock();
}
