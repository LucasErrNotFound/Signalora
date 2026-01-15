using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Signalora.Converters;

public class ActivityTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string type)
        {
            return type.ToLower() switch
            {
                "success" => new SolidColorBrush(Color.Parse("#D97757")),
                "warning" => new SolidColorBrush(Color.Parse("#FABD2F")),
                "error" => new SolidColorBrush(Color.Parse("#FB4934")),
                _ => new SolidColorBrush(Color.Parse("#D97757"))
            };
        }

        return new SolidColorBrush(Color.Parse("#D97757"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}