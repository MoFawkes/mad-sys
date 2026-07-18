using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
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
        SetResourceReference(BackgroundProperty, "WindowBrush");
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
}
