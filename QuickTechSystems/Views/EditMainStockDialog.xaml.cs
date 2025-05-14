// Path: QuickTechSystems.WPF.Views/EditMainStockDialog.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.ViewModels;

namespace QuickTechSystems.WPF.Views
{
    public partial class EditMainStockDialog : Window
    {
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