using System;
using System.Globalization;
using System.Windows.Data;

namespace KawaiiStudio.App.Converters;

public sealed class BoolToStatusConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "Connected" : "Disconnected";
        }

        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
