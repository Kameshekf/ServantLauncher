using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ServantInstaller
{
    public class IntEqualsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intVal && parameter != null)
            {
                if (parameter is int targetInt) return intVal == targetInt;
                if (int.TryParse(parameter.ToString(), out int parsed)) return intVal == parsed;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b && parameter != null && int.TryParse(parameter.ToString(), out int res))
            {
                return res;
            }
            return null;
        }
    }
}
