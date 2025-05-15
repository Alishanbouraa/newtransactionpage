// Path: QuickTechSystems.WPF.Views/EditMainStockDialog.xaml.cs
using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class EditMainStockDialog : Window
    {
        // Add regex patterns for validation
        private static readonly Regex _decimalRegex = new Regex(@"^[0-9]*(?:\.[0-9]*)?$");
        private static readonly Regex _integerRegex = new Regex(@"^[0-9]*$");

        public EditMainStockDialog()
        {
            InitializeComponent();
            Loaded += EditMainStockDialog_Loaded;
        }

        private void EditMainStockDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure window is maximized on load
            this.WindowState = WindowState.Maximized;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private void BarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (DataContext is EditMainStockViewModel viewModel)
                {
                    viewModel.LookupProductCommand.Execute(viewModel.EditingItem);
                    ValidateBarcodePair(viewModel.EditingItem);
                    e.Handled = true;
                }
            }
        }

        private void BoxBarcodeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Tab)
            {
                if (DataContext is EditMainStockViewModel viewModel)
                {
                    viewModel.LookupBoxBarcodeCommand.Execute(viewModel.EditingItem);
                    ValidateBarcodePair(viewModel.EditingItem);
                    e.Handled = true;
                }
            }
        }

        // Validate barcode pair
        private void ValidateBarcodePair(MainStockDTO item)
        {
            if (item == null) return;

            // Only validate when both fields have values
            if (!string.IsNullOrWhiteSpace(item.Barcode) && !string.IsNullOrWhiteSpace(item.BoxBarcode))
            {
                // If box barcode equals item barcode, automatically prefix it with "BX"
                if (item.BoxBarcode == item.Barcode)
                {
                    item.BoxBarcode = $"BX{item.Barcode}";
                }
            }
        }

        private void Barcode_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is EditMainStockViewModel viewModel)
            {
                // Validate barcodes when focus leaves either field
                ValidateBarcodePair(viewModel.EditingItem);
            }
        }

        // Handle image preview on mouse down
        private void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Image image && image.Source is BitmapImage bitmapImage)
            {
                try
                {
                    // Create a new instance of BitmapImage for the preview (to avoid sharing the same instance)
                    BitmapImage previewImage = new BitmapImage();
                    previewImage.BeginInit();
                    previewImage.UriSource = bitmapImage.UriSource;
                    previewImage.CacheOption = BitmapCacheOption.OnLoad;
                    previewImage.EndInit();

                    // Create and show a simple image preview window
                    Window previewWindow = new Window
                    {
                        Title = "Image Preview",
                        Owner = this,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Width = 500,
                        Height = 500,
                        ResizeMode = ResizeMode.CanResize,
                        Content = new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            Content = new Image
                            {
                                Source = previewImage,
                                Stretch = System.Windows.Media.Stretch.None
                            }
                        }
                    };

                    previewWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error showing image preview: {ex.Message}",
                        "Preview Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Numeric validation for decimal input (prices)
        private void DecimalTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text.Insert(textBox.CaretIndex, e.Text);
            e.Handled = !_decimalRegex.IsMatch(newText);
        }

        // Numeric validation for integer input (quantities)
        private void IntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var textBox = sender as TextBox;
            var newText = textBox.Text.Insert(textBox.CaretIndex, e.Text);
            e.Handled = !_integerRegex.IsMatch(newText);
        }

        // Allow paste only for valid numeric input
        private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                var textBox = sender as TextBox;
                var isInteger = (string)textBox.Tag == "integer";
                var regex = isInteger ? _integerRegex : _decimalRegex;

                if (!regex.IsMatch(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        // Price formatting when focus enters
        private void PriceTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Remove currency formatting and get the raw value
                string rawValue = textBox.Text.Replace("$", "").Replace(",", "").Trim();

                // If the value is 0 or 0.00, clear the text
                if (decimal.TryParse(rawValue, out decimal value) && value == 0)
                {
                    textBox.Text = string.Empty;
                }
            }
        }

        // Price formatting when focus leaves
        private void PriceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // If empty, set to 0 to trigger the StringFormat in the binding
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = "0";
                }
                // Ensure proper decimal format with dot
                else if (decimal.TryParse(textBox.Text, out decimal value))
                {
                    textBox.Text = value.ToString("0.00").Replace(",", ".");
                }
            }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);

            // Set up a binding to automatically close the dialog when the ViewModel sets DialogResult
            if (DataContext is EditMainStockViewModel viewModel)
            {
                viewModel.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(EditMainStockViewModel.DialogResult) &&
                        viewModel.DialogResult.HasValue)
                    {
                        this.DialogResult = viewModel.DialogResult.Value;
                        this.Close();
                    }
                };
            }
        }
    }
}