using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using AqiClock.App.ViewModels;
using AqiClock.App.Services;
using AqiClock.Application.Abstractions;

namespace AqiClock.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settings;
    private bool _allowClose;

    public MainWindow(MainViewModel viewModel, ISettingsService settings)
    {
        InitializeComponent(); _viewModel = viewModel; _settings = settings; DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelChanged; Loaded += OnLoaded;
    }

    public void AllowClose() => _allowClose = true;
    private async void OnLoaded(object sender, RoutedEventArgs e) { ApplyMode(); await _viewModel.InitializeAsync(); }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(MainViewModel.DisplayMode) or nameof(MainViewModel.IsAnnouncementsOpen) or nameof(MainViewModel.AlwaysOnTop)) ApplyMode(); }
    private void ApplyMode()
    {
        bool compact = _viewModel.DisplayMode == DisplayMode.Compact;
        WindowLayout layout = WindowLayouts.For(_viewModel.DisplayMode);
        WindowState = WindowState.Normal;
        MinWidth = layout.MinimumWidth; MinHeight = layout.MinimumHeight; Width = layout.Width; Height = layout.Height;
        WindowStyle = layout.IsFrameless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = layout.IsFrameless ? ResizeMode.NoResize : ResizeMode.CanResize;
        NormalLayout.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactLayout.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        AnnouncementPanel.Visibility = _viewModel.IsAnnouncementsOpen && !compact ? Visibility.Visible : Visibility.Collapsed;
        Topmost = _viewModel.AlwaysOnTop;
        RestorePlacement(compact ? _settings.Current.CompactPlacement : _settings.Current.NormalPlacement, !compact);
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
    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (_viewModel.DisplayMode == DisplayMode.Compact) { if (e.ClickCount == 2) _viewModel.ToggleDisplayModeCommand.Execute(null); else DragMove(); } }
    private async void OnPlacementChanged(object sender, EventArgs e) { if (!IsLoaded || WindowState == WindowState.Minimized) return; WindowPlacement value = new(Left, Top, ActualWidth, ActualHeight, WindowState == WindowState.Maximized); AppSettings current = _settings.Current; await _settings.SaveAsync(_viewModel.DisplayMode == DisplayMode.Compact ? current with { CompactPlacement = value } : current with { NormalPlacement = value }); }
}
