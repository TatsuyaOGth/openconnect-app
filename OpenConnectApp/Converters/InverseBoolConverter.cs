using Avalonia.Data.Converters;
using System.Globalization;

namespace OpenConnectApp.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : null;
}
