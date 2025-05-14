using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Handle string values for visibility
            if (value is string strValue)
            {
                return string.IsNullOrWhiteSpace(strValue) ? Visibility.Collapsed : Visibility.Visible;
            }

            // Handle numeric values for color styling
            if (value is decimal || value is double || value is float || value is int)
            {
                double numValue = System.Convert.ToDouble(value);

                if (parameter is string param)
                {
                    if (param.Equals("negative", StringComparison.OrdinalIgnoreCase))
                    {
                        return numValue < 0;
                    }
                    else if (param.Equals("positive", StringComparison.OrdinalIgnoreCase))
                    {
                        return numValue > 0;
                    }
                }

                return numValue != 0;
            }

            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}