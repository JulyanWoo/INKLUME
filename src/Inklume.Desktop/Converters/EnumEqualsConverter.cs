using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Inklume.Desktop.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DependencyProperty.UnsetValue;
}
