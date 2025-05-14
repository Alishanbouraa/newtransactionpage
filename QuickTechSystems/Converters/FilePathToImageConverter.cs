// Path: QuickTechSystems.WPF.Converters/FilePathToImageConverter.cs
using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace QuickTechSystems.WPF.Converters
{
    public class FilePathToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return null;

            try
            {
                string imagePath = value.ToString();

                // Check if this is a relative path
                if (!Path.IsPathRooted(imagePath))
                {
                    // Combine with base application path - this could also use a service to get the full path
                    string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    string productImagesDirectory = Path.Combine(baseDirectory, "ProductImages");
                    imagePath = Path.Combine(productImagesDirectory, imagePath);
                }

                if (!File.Exists(imagePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Image file not found: {imagePath}");
                    return null;
                }

                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(imagePath);

                // If sizing parameter is provided, set DecodePixelWidth
                if (parameter != null && int.TryParse(parameter.ToString(), out int size))
                {
                    image.DecodePixelWidth = size;
                }

                image.EndInit();
                image.Freeze(); // Important for cross-thread usage

                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error converting image path: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}