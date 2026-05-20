using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mono.Converters;

public sealed class PositionToOffsetConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is double ratio && values[1] is double width)
        {
            double offset = ratio * (width - 2);
            return offset < 0 ? 0.0 : offset;
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
