using System.Windows;
using AqiClock.Application.Abstractions;

namespace AqiClock.App.Services;

public sealed class ThemeService
{
    private AppTheme? _applied;
    public void Apply(AppTheme theme)
    {
        _applied = theme;
        AppTheme effective = theme == AppTheme.System
            ? (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) as int? == 0 ? AppTheme.Dark : AppTheme.Light)
            : theme;
        ResourceDictionary dictionary = new() { Source = new Uri($"Themes/{effective}.xaml", UriKind.Relative) };
        System.Windows.Application.Current.Resources.MergedDictionaries.Clear();
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(dictionary);
    }
}
