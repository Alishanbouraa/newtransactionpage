// Path: QuickTechSystems.WPF.Converters/MultiValueStockToBoxesConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;

namespace QuickTechSystems.WPF.Converters
{
    public class MultiValueStockToBoxesConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // First value should be the stock amount
            // Second value should be the items per box
            if (values.Length < 2 || values[0] == null || values[1] == null)
                return "0";

            if (!decimal.TryParse(values[0].ToString(), out decimal stock))
                return "0";

            if (!int.TryParse(values[1].ToString(), out int itemsPerBox) || itemsPerBox <= 0)
                itemsPerBox = 0;

            // Calculate and return the number of boxes
            return Math.Floor(stock / itemsPerBox).ToString("N0");
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}