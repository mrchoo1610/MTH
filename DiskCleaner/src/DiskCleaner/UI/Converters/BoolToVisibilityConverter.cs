using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskCleaner.UI.Converters;

/// <summary>true -> Visible, false -> Collapsed. ConverterParameter="invert" để đảo.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool v && v;
        if (parameter is string s && s == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
