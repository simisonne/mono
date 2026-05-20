using System.Globalization;
using System.Windows.Data;

namespace mono.Converters;

public sealed class TitleDisplayConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string artist && values[1] is string title)
        {
            return string.IsNullOrWhiteSpace(artist) ? title : $"{artist} — {title}";
        }
        return string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
