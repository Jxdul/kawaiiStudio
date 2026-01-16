using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KawaiiStudio.App.Converters;

public sealed class UsageToColorConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double usage)
        {
            return new SolidColorBrush(Colors.Gray);
        }

        if (usage < 50)
        {
            return new SolidColorBrush(Colors.Green);
        }

        if (usage < 80)
        {
            return new SolidColorBrush(Colors.Orange);
        }

        return new SolidColorBrush(Colors.Red);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
