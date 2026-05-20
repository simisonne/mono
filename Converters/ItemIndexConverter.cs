using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace mono.Converters;

public sealed class ItemIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ListViewItem item)
        {
            var lv = ItemsControl.ItemsControlFromItemContainer(item) as ListView;
            if (lv != null)
            {
                int index = lv.ItemContainerGenerator.IndexFromContainer(item);
                return (index + 1).ToString("D2");
            }
        }
        return "00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
