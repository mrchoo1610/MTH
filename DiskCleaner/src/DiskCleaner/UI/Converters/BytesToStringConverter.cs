using System.Globalization;
using System.Windows.Data;
using DiskCleaner.Core;

namespace DiskCleaner.UI.Converters;

public sealed class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long l ? SizeFormatter.Format(l) : value is int i ? SizeFormatter.Format(i) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
