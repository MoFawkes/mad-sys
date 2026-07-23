using System.Windows;
using System.Windows.Media;
using AqiClock.Application.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AqiClock.App.Services;

public sealed class ThemeService
{
    private static readonly Color BrandBlue = Color.FromRgb(0x2E, 0x6D, 0xD8);
    private AppTheme? _applied;

    public void Apply(AppTheme theme)
    {
        if (_applied == theme) return;
        _applied = theme;
        AppTheme effective = theme == AppTheme.System
            ? (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) as int? == 0 ? AppTheme.Dark : AppTheme.Light)
            : theme;

        ApplicationTheme fluentTheme = effective == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        // The palette dictionary must be in place before ApplicationThemeManager.Changed
        // handlers (MainWindow titlebar/native-border sync) read brushes from it.
        SwapApplicationThemeDictionary(effective);
        ApplicationThemeManager.Apply(fluentTheme, WindowBackdropType.None, updateAccent: false);
        ApplicationAccentColorManager.Apply(BrandBlue, fluentTheme, systemGlassColor: false, systemAccentColor: false);
    }

    private static void SwapApplicationThemeDictionary(AppTheme effective)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        ResourceDictionary replacement = new()
        {
            Source = new Uri($"/AqiClock.App;component/Themes/{effective}.xaml", UriKind.RelativeOrAbsolute),
        };

        for (int index = 0; index < dictionaries.Count; index++)
        {
            string? source = dictionaries[index].Source?.OriginalString;
            if (source?.EndsWith("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true
                || source?.EndsWith("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true
                || source?.Equals("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) == true
                || source?.Equals("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) == true)
            {
                dictionaries[index] = replacement;
                return;
            }
        }

        dictionaries.Add(replacement);
    }
}
