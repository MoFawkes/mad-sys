using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace AqiClock.Application.Tests;

internal static class WpfUiTestResources
{
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
        window.Resources.MergedDictionaries.Add(new ControlsDictionary());
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/AqiClock.App;component/Themes/Light.xaml", UriKind.RelativeOrAbsolute),
        });
    }
}
