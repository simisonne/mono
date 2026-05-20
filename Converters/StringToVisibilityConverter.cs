using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mono.Converters;

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter,
        CultureInfo culture) =>
        value is string s && s.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter,
        CultureInfo culture) =>
        throw new NotImplementedException();
}
