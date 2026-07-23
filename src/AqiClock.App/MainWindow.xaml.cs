using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AqiClock.App.ViewModels;
using AqiClock.App.Services;
using AqiClock.Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace AqiClock.App;

public partial class MainWindow : Window
{
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settings;
    private readonly ILogger<MainWindow> _logger;
    private readonly DispatcherTimer _placementTimer;
    private bool _allowClose;
    private bool _applyingMode;
    private int _modeTransition;
    private DisplayMode _pendingPlacementMode;
    private WindowPlacement? _pendingPlacement;

    public MainWindow(MainViewModel viewModel, ISettingsService settings, ILogger<MainWindow> logger)
    {
        InitializeComponent(); _viewModel = viewModel; _settings = settings; _logger = logger; DataContext = viewModel;
        _placementTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        _placementTimer.Tick += OnPlacementTimerTick;
        viewModel.PropertyChanged += OnViewModelChanged;
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    public void AllowClose() => _allowClose = true;
    private async void OnLoaded(object sender, RoutedEventArgs e) { ApplyMode(); await _viewModel.InitializeAsync(); }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        ApplyTitleBarTheme(ApplicationThemeManager.GetAppTheme());
    }
    private void OnApplicationThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
        => ApplyTitleBarTheme(currentApplicationTheme);
    private void ApplyTitleBarTheme(ApplicationTheme theme)
    {
        if (WindowStyle == WindowStyle.None) return;
        if (theme == ApplicationTheme.Dark) WindowBackgroundManager.ApplyDarkThemeToWindow(this);
        else WindowBackgroundManager.RemoveDarkThemeFromWindow(this);
        // The dark-theme helper applies a backdrop to this plain window, which makes
        // the DWM caption/border colors inert until the frame is rebuilt. Strip it.
        _ = Wpf.Ui.Controls.WindowBackdrop.RemoveBackdrop(this);
        SetResourceReference(BackgroundProperty, "WindowBrush");
        ApplyNativeBorderColor();
        // WPF-UI's theme application resets the DWM caption/border colors after our
        // Changed handler runs (verified live: an external DwmSetWindowAttribute call
        // renders instantly, in-handler calls get stomped). Re-apply after its pass.
        ScheduleNativeColorRetry();
    }

    private System.Windows.Threading.DispatcherTimer? _nativeColorRetry;
    private int _nativeColorRetriesLeft;
    private void ScheduleNativeColorRetry()
    {
        if (_nativeColorRetry is null)
        {
            // WPF-UI's theme transition finishes well after the Changed event, so a
            // single early retry loses the race; retry until the transition settles.
            _nativeColorRetry = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _nativeColorRetry.Tick += (_, _) =>
            {
                ApplyNativeBorderColor();
                if (--_nativeColorRetriesLeft <= 0) _nativeColorRetry!.Stop();
            };
        }

        _nativeColorRetriesLeft = 5;
        _nativeColorRetry.Stop();
        _nativeColorRetry.Start();
    }

    private void ApplyNativeBorderColor()
    {
        if (WindowStyle == WindowStyle.None || TryFindResource("WindowBrush") is not SolidColorBrush brush) return;
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        Color color = brush.Color;
        int colorReference = color.R | color.G << 8 | color.B << 16;
        _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref colorReference, sizeof(int));
        _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref colorReference, sizeof(int));
        if (TryFindResource("TextBrush") is SolidColorBrush textBrush)
        {
            Color text = textBrush.Color;
            int textReference = text.R | text.G << 8 | text.B << 16;
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textReference, sizeof(int));
        }

        // The colors only take effect once the non-client frame is refreshed.
        _ = SetWindowPos(handle, 0, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }
    private void OnClosed(object? sender, EventArgs e)
        => ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.DisplayMode)) ApplyMode();
        else if (e.PropertyName == nameof(MainViewModel.IsAnnouncementsOpen)) AnnouncementPanel.Visibility = _viewModel.IsAnnouncementsOpen && _viewModel.DisplayMode == DisplayMode.Normal ? Visibility.Visible : Visibility.Collapsed;
        else if (e.PropertyName == nameof(MainViewModel.AlwaysOnTop)) Topmost = _viewModel.AlwaysOnTop;
    }
    private void ApplyMode()
    {
        _applyingMode = true;
        _placementTimer.Stop();
        int transition = ++_modeTransition;
        DisplayMode targetMode = _viewModel.DisplayMode;
        bool compact = _viewModel.DisplayMode == DisplayMode.Compact;
        WindowLayout layout = WindowLayouts.For(_viewModel.DisplayMode);
        WindowState = WindowState.Normal;
        MinWidth = layout.MinimumWidth; MinHeight = layout.MinimumHeight; Width = layout.Width; Height = layout.Height;
        WindowStyle = layout.IsFrameless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = layout.IsFrameless ? ResizeMode.NoResize : ResizeMode.CanResize;
        ApplyTitleBarTheme(ApplicationThemeManager.GetAppTheme());
        NormalLayout.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactLayout.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        AnnouncementPanel.Visibility = _viewModel.IsAnnouncementsOpen && !compact ? Visibility.Visible : Visibility.Collapsed;
        Topmost = _viewModel.AlwaysOnTop;
        RestorePlacement(compact ? _settings.Current.CompactPlacement : _settings.Current.NormalPlacement, !compact);
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (transition != _modeTransition) return;
            _applyingMode = false;
            QueuePlacement(targetMode);
        });
    }
    private void RestorePlacement(WindowPlacement? placement, bool restoreSize)
    {
        if (placement is null) return;
        Rect work = SystemParameters.WorkArea;
        if (placement.Left < work.Right && placement.Top < work.Bottom && placement.Left + placement.Width > work.Left && placement.Top + placement.Height > work.Top)
        { Left = placement.Left; Top = placement.Top; if (restoreSize) { Width = Math.Max(MinWidth, placement.Width); Height = Math.Max(MinHeight, placement.Height); WindowState = placement.IsMaximized ? WindowState.Maximized : WindowState.Normal; } }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!_allowClose && _settings.Current.CloseToTray) { e.Cancel = true; WindowState = WindowState.Minimized; Hide(); } }
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (_viewModel.DisplayMode == DisplayMode.Compact && e.ClickCount == 2) _viewModel.ToggleDisplayModeCommand.Execute(null); }
    private void OnMouseMove(object sender, MouseEventArgs e) { if (_viewModel.DisplayMode == DisplayMode.Compact && e.LeftButton == MouseButtonState.Pressed && e.Source is not System.Windows.Controls.Primitives.ButtonBase) DragMove(); }
    private void OnPlacementChanged(object sender, EventArgs e) { if (!_applyingMode) QueuePlacement(_viewModel.DisplayMode); }
    private void QueuePlacement(DisplayMode mode)
    {
        if (!IsLoaded || WindowState == WindowState.Minimized) return;
        _pendingPlacementMode = mode;
        _pendingPlacement = new WindowPlacement(Left, Top, ActualWidth, ActualHeight, WindowState == WindowState.Maximized);
        _placementTimer.Stop(); _placementTimer.Start();
    }
    private async void OnPlacementTimerTick(object? sender, EventArgs e)
    {
        _placementTimer.Stop();
        if (_pendingPlacement is not { } placement) return;
        try { await _settings.SaveAsync(WindowPlacements.Apply(_settings.Current, _pendingPlacementMode, placement)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { LogPlacementSaveFailed(_logger, exception); }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not persist window placement")]
    private static partial void LogPlacementSaveFailed(ILogger logger, Exception exception);

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int width, int height, uint flags);
}
