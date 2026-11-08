using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Wpf.Ui.Appearance;
using AqiClock.App.Services;

namespace AqiClock.App.Views;

public sealed class ListenLiveWindow : Window, IDisposable
{
    private const string PlayerUrl = EMasjidLiveStatus.PlayerUrl;
    private const string ListenUrl = "https://emasjidlive.co.uk/listen/alqalaminstitute";
    private readonly WebView2 _player = new();
    private readonly TextBlock _status = new() { Text = "Loading player…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) };
    private bool _closed;
    private bool _initializing;

    public ListenLiveWindow()
    {
        Title = "Listen live — AQI Clock";
        Width = 560; Height = 420; MinWidth = 380; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        var layout = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Al-Qalam Institute", FontSize = 22, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(_status);
        DockPanel.SetDock(heading, Dock.Top);
        layout.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var reload = new Wpf.Ui.Controls.Button
        {
            Content = "Reload player", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 36, Padding = new Thickness(14, 6, 14, 6),
        };
        reload.Click += async (_, _) => await LoadPlayerAsync();
        var website = new Wpf.Ui.Controls.Button
        {
            Content = "Open website", Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            MinHeight = 36, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0),
        };
        website.Click += (_, _) => OpenWebsite(ListenUrl);
        actions.Children.Add(reload); actions.Children.Add(website);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        layout.Children.Add(_player);
        Content = layout;
        Loaded += async (_, _) =>
        {
            ApplicationThemeManager.Changed += OnThemeChanged;
            UpdatePlayerBackground();
            await LoadPlayerAsync();
        };
        Closed += (_, _) => Dispose();
    }

    private async Task LoadPlayerAsync()
    {
        if (_initializing || _closed) return;
        _initializing = true;
        _status.Text = "Loading player…";
        try
        {
            if (_player.CoreWebView2 is null)
            {
                string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqiClock", "eMasjidPlayer");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
                if (_closed) return;
                await _player.EnsureCoreWebView2Async(environment);
                if (_closed) return;
                CoreWebView2 browser = _player.CoreWebView2 ?? throw new InvalidOperationException("Player initialization failed.");
                browser.Settings.IsWebMessageEnabled = false;
                browser.NewWindowRequested += (_, e) => { e.Handled = true; OpenWebsite(e.Uri); };
                browser.NavigationStarting += (_, e) =>
                {
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri)
                        && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "emasjidlive.co.uk"
                        && uri.AbsolutePath == "/miniplayer/alqalaminstitute") return;
                    e.Cancel = true;
                    OpenWebsite(e.Uri);
                };
                browser.NavigationCompleted += (_, e) => _status.Text = e.IsSuccess
                    ? "Press Play when the channel is live. Close this window to stop listening."
                    : "Could not load the player. Check your internet connection and try Reload player.";
            }
            UpdatePlayerBackground();
            string theme = IsDarkTheme() ? "dark" : "light";
            _player.CoreWebView2!.Navigate($"{PlayerUrl}?theme={theme}");
        }
        catch (Exception exception) when (exception is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            if (!_closed) _status.Text = "The player could not start. Try Reload player, or Open website. Microsoft Edge WebView2 Runtime is required for listening here.";
        }
        finally { _initializing = false; }
    }

    private bool IsDarkTheme() => Background is SolidColorBrush brush
        && (0.2126 * brush.Color.R + 0.7152 * brush.Color.G + 0.0722 * brush.Color.B) < 128;

    private void OnThemeChanged(ApplicationTheme theme, Color accent) => UpdatePlayerBackground();

    private void UpdatePlayerBackground()
    {
        if (_closed) return;
        if (Background is SolidColorBrush brush)
            _player.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, brush.Color.R, brush.Color.G, brush.Color.B);
        if (IsDarkTheme()) WindowBackgroundManager.ApplyDarkThemeToWindow(this);
        else WindowBackgroundManager.RemoveDarkThemeFromWindow(this);
        // Use the new official player theme on the next reload. Changing the
        // app theme must not navigate away from a broadcast already playing.
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        ApplicationThemeManager.Changed -= OnThemeChanged;
        _player.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OpenWebsite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { _status.Text = "Could not open your browser. Try Reload player."; }
    }
}
