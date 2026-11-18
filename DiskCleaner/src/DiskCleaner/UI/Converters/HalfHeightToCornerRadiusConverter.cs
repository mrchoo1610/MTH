using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiskCleaner.UI.Converters;

/// <summary>ActualHeight -> CornerRadius(h/2): bo tròn kiểu pill. CornerRadius quá lớn (999) làm WPF vẽ góc hình elip, nên cần bán kính đúng.</summary>
public sealed class HalfHeightToCornerRadiusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new CornerRadius(value is double h && h > 0 ? h / 2 : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
