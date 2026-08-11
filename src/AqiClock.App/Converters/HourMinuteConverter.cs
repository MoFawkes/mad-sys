using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AqiClock.App.Converters;

/// <summary>
/// Shows period times as HH:mm and accepts the same. Lesson times are never finer than a minute,
/// so the seconds in the default <see cref="TimeSpan"/> rendering are noise the admin has to skip past.
/// </summary>
/// <remarks>
/// Parsing is deliberately stricter than <see cref="TimeSpan.Parse(string, IFormatProvider)"/>, which
/// reads a bare "1" as one day and "26:00" as twenty-six hours. Both are silent nonsense in a timetable,
/// so only H:mm and HH:mm within a single day are accepted; anything else fails the conversion and the
/// cell shows its validation state instead of committing a wrong value.
/// </remarks>
public sealed class HourMinuteConverter : IValueConverter
{
    private static readonly string[] AcceptedFormats = ["H:mm", "HH:mm"];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is TimeSpan span && span >= TimeSpan.Zero && span < TimeSpan.FromDays(1)
            ? TimeOnly.FromTimeSpan(span).ToString("HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text
        && TimeOnly.TryParseExact(text.Trim(), AcceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly parsed)
            ? parsed.ToTimeSpan()
            : DependencyProperty.UnsetValue;
}
