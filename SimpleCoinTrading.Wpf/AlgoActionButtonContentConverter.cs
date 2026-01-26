using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SimpleCoinTrading.Wpf;

public class AlgoActionButtonContentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string isRunning)
        {
            if (isRunning == "Running")
            {
                return "Stop";
            }

            if (isRunning == "Stopped")
            {
                return "Start";
            }
        }
        
        return DependencyProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}