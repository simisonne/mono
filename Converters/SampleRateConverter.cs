using System.Globalization;
using System.Windows.Data;

namespace mono.Converters;

public sealed class SampleRateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int rate && rate > 0)
            return (rate / 1000.0).ToString("0.0##", CultureInfo.InvariantCulture) + "kHz";
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
