using AqiClock.Application.Abstractions;
using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace AqiClock.App.Services;

public sealed record WindowLayout(double Width, double Height, double MinimumWidth, double MinimumHeight, bool IsFrameless);

public static class WindowLayouts
{
    public static WindowLayout For(DisplayMode mode) => mode switch
    {
        DisplayMode.Normal => new(820, 560, 700, 500, false),
        DisplayMode.Compact => new(320, 80, 320, 80, true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

public sealed class WindowPlacementController : IDisposable
{
    private readonly Window _window;
    private readonly ISettingsService _settings;
    private readonly Func<AppSettings, WindowPlacement?> _read;
    private readonly Func<AppSettings, WindowPlacement, AppSettings> _write;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };

    public WindowPlacementController(Window window, ISettingsService settings, Func<AppSettings, WindowPlacement?> read, Func<AppSettings, WindowPlacement, AppSettings> write)
    {
        _window = window; _settings = settings; _read = read; _write = write;
        _timer.Tick += Save;
        window.Loaded += Restore;
        window.LocationChanged += Queue;
        window.SizeChanged += Queue;
        window.Closed += (_, _) => Dispose();
    }

    private void Restore(object sender, RoutedEventArgs args)
    {
        Rect work = SystemParameters.WorkArea;
        WindowPlacement requested = _read(_settings.Current) ?? new(
            work.Left + Math.Max(0, (work.Width - _window.Width) / 2),
            work.Top + Math.Max(0, (work.Height - _window.Height) / 2),
            _window.Width, _window.Height);
        WindowPlacement placement = WindowPlacements.ClampToWorkArea(requested, _window.MinWidth, _window.MinHeight);
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Left = placement.Left; _window.Top = placement.Top;
        _window.Width = placement.Width; _window.Height = placement.Height;
        if (placement.IsMaximized) _window.WindowState = WindowState.Maximized;
    }

    private void Queue(object? sender, EventArgs args)
    {
        if (!_window.IsLoaded || _window.WindowState == WindowState.Minimized) return;
        _timer.Stop(); _timer.Start();
    }

    private async void Save(object? sender, EventArgs args)
    {
        _timer.Stop();
        var placement = new WindowPlacement(_window.Left, _window.Top, _window.ActualWidth, _window.ActualHeight, _window.WindowState == WindowState.Maximized);
        try { await _settings.SaveAsync(_write(_settings.Current, placement)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _timer.Stop();
        _window.Loaded -= Restore; _window.LocationChanged -= Queue; _window.SizeChanged -= Queue;
        GC.SuppressFinalize(this);
    }
}

public static class WindowLifecycle
{
    public static bool ShouldExitAfterSignInClose(SessionState session, bool returnToRoleChoice = false) =>
        session.UserId is null && !returnToRoleChoice;
    public static ActivationTarget TargetForActivation(SessionState session, bool recoveryVisible, bool studentSessionActive = false) =>
        recoveryVisible ? ActivationTarget.PasswordRecovery :
        session.UserId is null && !studentSessionActive ? ActivationTarget.SignIn :
        ActivationTarget.Main;
}

public enum ActivationTarget { SignIn, PasswordRecovery, Main }

public static class WindowPlacements
{
    public static WindowPlacement Clamp(WindowPlacement placement, double workLeft, double workTop, double workWidth, double workHeight, double minimumWidth, double minimumHeight)
    {
        double width = Math.Min(Math.Max(minimumWidth, placement.Width), workWidth);
        double height = Math.Min(Math.Max(minimumHeight, placement.Height), workHeight);
        double left = Math.Clamp(placement.Left, workLeft, workLeft + workWidth - width);
        double top = Math.Clamp(placement.Top, workTop, workTop + workHeight - height);
        return placement with { Left = left, Top = top, Width = width, Height = height };
    }

    public static WindowPlacement ClampToWorkArea(WindowPlacement placement, double minimumWidth, double minimumHeight)
    {
        System.Windows.Rect work = System.Windows.SystemParameters.WorkArea;
        return Clamp(placement, work.Left, work.Top, work.Width, work.Height, minimumWidth, minimumHeight);
    }

    public static AppSettings Apply(AppSettings settings, DisplayMode mode, WindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WindowPlacement normalized = mode == DisplayMode.Compact
            ? placement with { Width = WindowLayouts.For(DisplayMode.Compact).Width, Height = WindowLayouts.For(DisplayMode.Compact).Height, IsMaximized = false }
            : placement;
        return mode == DisplayMode.Compact
            ? settings with { CompactPlacement = normalized }
            : settings with { NormalPlacement = normalized };
    }
}
