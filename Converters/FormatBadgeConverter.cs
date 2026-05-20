using System.Globalization;
using System.Windows.Data;

namespace mono.Converters;

public sealed class FormatBadgeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 4) return "";

        string format = values[0] as string ?? "";
        string sampleRate = values[1] as string ?? "";
        int bitDepth = values[2] is int b ? b : 0;
        int bitrate = values[3] is int br ? br : 0;

        if (string.IsNullOrEmpty(format)) return "";

        string bitratePart = bitrate > 0 ? $" · {bitrate}kbps" : "";
        string bitDepthPart = bitDepth > 0 ? $" · {bitDepth}bit" : "";

        if (!string.IsNullOrEmpty(sampleRate))
            return $"{format} · {sampleRate}{bitratePart}{bitDepthPart}";

        return $"{format}{bitratePart}{bitDepthPart}".TrimStart(' ', '·');
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
