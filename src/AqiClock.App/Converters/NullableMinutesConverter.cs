using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AqiClock.App.Converters;

/// <summary>
/// Renders nullable minute values as ordinary integers and commits an empty cell as null.
/// WPF's default nullable-number conversion treats an empty DataGrid cell as a validation
/// error before the source can see it, which made an intentionally unset prayer duration
/// impossible to save.
/// </summary>
public sealed class NullableMinutesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int minutes ? minutes.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text) return DependencyProperty.UnsetValue;
        text = text.Trim();
        if (text.Length == 0) return null;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)
            ? minutes
            : DependencyProperty.UnsetValue;
    }
}
