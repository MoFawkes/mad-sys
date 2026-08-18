using AqiClock.Application.Abstractions;
using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

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

public sealed partial class WindowPlacementController : IDisposable
{
    private readonly Window _window;
    private readonly ISettingsService _settings;
    private readonly Func<AppSettings, WindowPlacement?> _read;
    private readonly Func<AppSettings, WindowPlacement, AppSettings> _write;
    private readonly ILogger<WindowPlacementController> _logger;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };

    public WindowPlacementController(Window window, ISettingsService settings, Func<AppSettings, WindowPlacement?> read, Func<AppSettings, WindowPlacement, AppSettings> write, ILogger<WindowPlacementController> logger)
    {
        _window = window; _settings = settings; _read = read; _write = write; _logger = logger;
        _timer.Tick += Save;
        window.Loaded += Restore;
        window.LocationChanged += Queue;
        window.SizeChanged += Queue;
        window.Closed += (_, _) => Dispose();
    }

    private void Restore(object sender, RoutedEventArgs args) => RestorePlacement();

    internal void RestorePlacement(WindowPlacement? currentPlacement = null, Rect? workArea = null)
    {
        WindowPlacement requested = _read(_settings.Current) ?? new WindowPlacement(
            currentPlacement?.Left ?? _window.Left,
            currentPlacement?.Top ?? _window.Top,
            currentPlacement?.Width ?? _window.ActualWidth,
            currentPlacement?.Height ?? _window.ActualHeight);

        WindowPlacement placement;
        try
        {
            Rect work = workArea ?? MonitorWorkAreas.ForPlacement(requested);
            placement = WindowPlacements.Clamp(
                requested, work.Left, work.Top, work.Width, work.Height,
                _window.MinWidth, _window.MinHeight);
        }
        catch (Exception exception) when (exception is OverflowException or Win32Exception or COMException or InvalidOperationException)
        {
            LogPlacementRestoreFailed(_logger, exception);
            return;
        }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not restore saved window placement; using window defaults")]
    private static partial void LogPlacementRestoreFailed(ILogger logger, Exception exception);
}

internal static class MonitorWorkAreas
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;
    private const double WpfDpi = 96d;

    internal static Rect ForPlacement(WindowPlacement placement)
    {
        var requested = new NativeRect(
            ToNativeCoordinate(placement.Left),
            ToNativeCoordinate(placement.Top),
            ToNativeCoordinate(placement.Left + placement.Width),
            ToNativeCoordinate(placement.Top + placement.Height));
        nint monitor = MonitorFromRect(ref requested, MonitorDefaultToNearest);
        if (monitor == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());

        int result = GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out uint dpiY);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        if (dpiX == 0 || dpiY == 0) throw new InvalidOperationException("The target monitor reported an invalid effective DPI.");

        double scaleX = WpfDpi / dpiX;
        double scaleY = WpfDpi / dpiY;
        return new Rect(
            info.Work.Left * scaleX,
            info.Work.Top * scaleY,
            (info.Work.Right - info.Work.Left) * scaleX,
            (info.Work.Bottom - info.Work.Top) * scaleY);
    }

    private static int ToNativeCoordinate(double value) => checked((int)Math.Round(value));

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromRect(ref NativeRect rectangle, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}

public static class WindowLifecycle
{
    public static bool ShouldExitAfterSignInClose(SessionState session, bool returnToRoleChoice = false) =>
        session.UserId is null && !returnToRoleChoice;
    public static ActivationTarget TargetForActivation(SessionState session, bool recoveryVisible, bool studentSessionActive = false) =>
        recoveryVisible ? ActivationTarget.PasswordRecovery :
        session.UserId is null && !studentSessionActive ? ActivationTarget.SignIn :
        ActivationTarget.Main;

    /// <summary>
    /// An enrolled student device is a display, so it always shows the clock at startup.
    /// Start-minimised only applies to staff machines, where the app is a background utility.
    /// </summary>
    public static bool ShouldShowMainWindowAtStartup(bool studentReady, bool startMinimisedRequested, bool startMinimisedSetting) =>
        studentReady || (!startMinimisedRequested && !startMinimisedSetting);
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
