// Path: QuickTechSystems.WPF.ViewModels/MainStockViewModel.BoxOperations.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class MainStockViewModel
    {
        /// <summary>
        /// Converts a box to individual items for the selected product.
        /// Decreases box count by 1 and increases individual items by ItemsPerBox.
        /// </summary>
        private async Task ConvertBoxToIndividualAsync()
        {
            // Use reasonable timeout instead of 0
            if (!await _operationLock.WaitAsync(DEFAULT_LOCK_TIMEOUT_MS))
            {
                ShowTemporaryErrorMessage("Operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedItem == null)
                {
                    ShowTemporaryErrorMessage("Please select an item first.");
                    return;
                }

                if (SelectedItem.NumberOfBoxes <= 0)
                {
                    ShowTemporaryErrorMessage("No boxes available to convert.");
                    return;
                }

                if (SelectedItem.ItemsPerBox <= 0)
                {
                    ShowTemporaryErrorMessage("This item has no items per box defined.");
                    return;
                }

                IsSaving = true;
                StatusMessage = "Converting box to individual items...";

                // Store original values for error handling and validation
                int mainStockId = SelectedItem.MainStockId;
                int originalBoxCount = SelectedItem.NumberOfBoxes;
                decimal originalIndividualCount = SelectedItem.CurrentStock;
                int itemsPerBox = SelectedItem.ItemsPerBox;

                // First step: Get current item data
                var currentItem = await _mainStockService.GetByIdAsync(mainStockId);

                if (currentItem == null || currentItem.NumberOfBoxes <= 0)
                {
                    throw new InvalidOperationException("Item not found or no boxes available");
                }

                // Second step: Create a modified copy with updated values
                var updatedItem = new Application.DTOs.MainStockDTO
                {
                    MainStockId = currentItem.MainStockId,
                    Name = currentItem.Name,
                    Barcode = currentItem.Barcode,
                    BoxBarcode = currentItem.BoxBarcode,
                    CategoryId = currentItem.CategoryId,
                    CategoryName = currentItem.CategoryName,
                    SupplierId = currentItem.SupplierId,
                    SupplierName = currentItem.SupplierName,
                    Description = currentItem.Description,
                    PurchasePrice = currentItem.PurchasePrice,
                    WholesalePrice = currentItem.WholesalePrice,
                    SalePrice = currentItem.SalePrice,
                    BoxPurchasePrice = currentItem.BoxPurchasePrice,
                    BoxWholesalePrice = currentItem.BoxWholesalePrice,
                    BoxSalePrice = currentItem.BoxSalePrice,

                    // Update these values
                    NumberOfBoxes = currentItem.NumberOfBoxes - 1,
                    CurrentStock = currentItem.CurrentStock + currentItem.ItemsPerBox,

                    // Keep these the same
                    ItemsPerBox = currentItem.ItemsPerBox,
                    MinimumStock = currentItem.MinimumStock,
                    MinimumBoxStock = currentItem.MinimumBoxStock,
                    BarcodeImage = currentItem.BarcodeImage,
                    Speed = currentItem.Speed,
                    IsActive = currentItem.IsActive,
                    ImagePath = currentItem.ImagePath,
                    CreatedAt = currentItem.CreatedAt,
                    UpdatedAt = DateTime.Now
                };

                // Third step: Update the item using the service
                var result = await _mainStockService.UpdateAsync(updatedItem);

                if (result == null)
                {
                    throw new InvalidOperationException("Failed to update stock.");
                }

                // Get updated stock from database to ensure we have accurate data
                var refreshedItem = await GetUpdatedMainStockItemAsync(mainStockId);

                if (refreshedItem != null)
                {
                    // Capture the itemsPerBox value before dispatcher operation
                    int displayItemsPerBox = refreshedItem.ItemsPerBox;
                    decimal newCurrentStock = refreshedItem.CurrentStock;
                    int newBoxCount = refreshedItem.NumberOfBoxes;

                    // Store display values separately in case SelectedItem becomes null
                    decimal displayCurrentStock = refreshedItem.CurrentStock;
                    int displayBoxCount = refreshedItem.NumberOfBoxes;

                    // Update the UI model with fresh data - WITH EXTRA NULL CHECKS
                    await SafeDispatcherOperation(() =>
                    {
                        // Check if SelectedItem is still not null when we update it
                        if (SelectedItem != null)
                        {
                            SelectedItem.NumberOfBoxes = newBoxCount;
                            SelectedItem.CurrentStock = newCurrentStock;

                            // Raise property changed notification for derived properties
                            OnPropertyChanged(nameof(SelectedItemBoxCount));
                        }
                    });

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Successfully converted 1 box to {displayItemsPerBox} individual items.\nNew stock: {displayCurrentStock} items, {displayBoxCount} boxes.",
                            "Conversion Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    });

                    // Refresh the view to show updated data
                    await RefreshFromDatabaseDirectly();
                }
                else
                {
                    throw new InvalidOperationException("Could not retrieve updated item data.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting box to individual items: {ex.Message}");
                ShowTemporaryErrorMessage($"Error converting box: {ex.Message}");
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }
    }
}