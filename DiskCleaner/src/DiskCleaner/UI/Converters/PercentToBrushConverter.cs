using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DiskCleaner.UI.Converters;

/// <summary>% đã dùng -> màu: xanh dưới 85, vàng 85-95, đỏ trên 95.</summary>
public sealed class PercentToBrushConverter : IValueConverter
{
    private static readonly Brush Ok = Freeze(0x22, 0xC5, 0x5E);
    private static readonly Brush Warn = Freeze(0xF5, 0xA5, 0x24);
    private static readonly Brush Bad = Freeze(0xEF, 0x44, 0x44);

    private static Brush Freeze(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var p = value is double d ? d : 0;
        return p > 95 ? Bad : p >= 85 ? Warn : Ok;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
