using System;
using System.Globalization;
using System.Windows.Data;

namespace KawaiiStudio.App.Converters;

public sealed class BoolToYesNoConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "Yes" : "No";
        }

        return "Unknown";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
