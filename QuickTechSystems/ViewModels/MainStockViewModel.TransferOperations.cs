// Path: QuickTechSystems.WPF.ViewModels/MainStockViewModel.TransferOperations.cs
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.Events;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class MainStockViewModel
    {
        private void ShowTransferDialog()
        {
            if (SelectedItem == null)
            {
                ShowTemporaryErrorMessage("Please select an item to transfer.");
                return;
            }

            TransferQuantity = 1;
            TransferByBoxes = false;
            _ = LoadStoreProductsAsync();
            OnPropertyChanged(nameof(SelectedItemBoxCount));
            IsTransferPopupOpen = true;
        }

        private async Task TransferToStoreAsync()
        {
            if (!await _operationLock.WaitAsync(DEFAULT_LOCK_TIMEOUT_MS))
            {
                ShowTemporaryErrorMessage("Transfer operation already in progress. Please wait.");
                return;
            }

            try
            {
                if (SelectedItem == null)
                {
                    ShowTemporaryErrorMessage("Please select an item to transfer.");
                    return;
                }

                if (SelectedStoreProduct == null)
                {
                    ShowTemporaryErrorMessage("Please select a store product to transfer to.");
                    return;
                }

                if (TransferQuantity <= 0)
                {
                    ShowTemporaryErrorMessage("Transfer quantity must be greater than zero.");
                    return;
                }

                // FIXED: Check against CurrentStock directly for individual items
                if (TransferByBoxes)
                {
                    if (TransferQuantity > SelectedItem.NumberOfBoxes)
                    {
                        ShowTemporaryErrorMessage($"Transfer quantity ({TransferQuantity} boxes) exceeds available box stock ({SelectedItem.NumberOfBoxes} boxes).");
                        return;
                    }
                }
                else
                {
                    // Use CurrentStock directly for individual items check
                    if (TransferQuantity > SelectedItem.CurrentStock)
                    {
                        ShowTemporaryErrorMessage($"Transfer quantity ({TransferQuantity}) exceeds available stock ({SelectedItem.CurrentStock}).");
                        return;
                    }
                }

                // Store item details before the transfer
                int mainStockId = SelectedItem.MainStockId;
                int productId = SelectedStoreProduct.ProductId;
                string itemName = SelectedItem.Name;

                IsSaving = true;
                StatusMessage = "Processing transfer...";

                string transferredBy = "System User";

                bool transferSuccessful = false;

                try
                {
                    transferSuccessful = await _mainStockService.TransferToStoreAsync(
                        mainStockId,
                        productId,
                        TransferQuantity,
                        transferredBy,
                        $"Manual transfer from MainStock to Store",
                        TransferByBoxes
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Transfer service error: {ex.Message}");
                    throw new InvalidOperationException($"Transfer failed: {ex.Message}", ex);
                }

                if (!transferSuccessful)
                {
                    throw new InvalidOperationException("Transfer failed. Please try again.");
                }

                // Close the transfer popup
                IsTransferPopupOpen = false;

                // Force a full UI refresh after small delay to ensure transaction is complete
                await Task.Delay(300);
                await RefreshFromDatabaseDirectly();
                _eventAggregator.Publish(new GlobalDataRefreshEvent());

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    string unitType = TransferByBoxes ? "boxes" : "units";
                    MessageBox.Show(
                        $"Successfully transferred {TransferQuantity} {unitType} of {itemName} from MainStock to Store inventory.",
                        "Transfer Successful",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Transfer error: {ex.Message}");
                string errorDetail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                string properErrorMessage = errorDetail.Replace("Payment failed:", "Transfer failed:");
                ShowTemporaryErrorMessage(properErrorMessage);
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
                _operationLock.Release();
            }
        }
        private async Task<MainStockDTO> GetUpdatedMainStockItemAsync(int mainStockId)
        {
            try
            {
                return await _mainStockService.GetByIdAsync(mainStockId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching updated MainStock item: {ex.Message}");
                return null;
            }
        }
    }
}