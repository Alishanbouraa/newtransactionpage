// Path: QuickTechSystems.WPF.ViewModels/BulkMainStockViewModel.Commands.cs
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.WPF.Views;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class BulkMainStockViewModel
    {
        /// <summary>
        /// Adds a new row to the Items collection.
        /// </summary>
        private void AddNewRow()
        {
            var newItem = new MainStockDTO
            {
                IsActive = true,
                CreatedAt = DateTime.Now,
                ItemsPerBox = 1, // Default to 1 item per box
                NumberOfBoxes = 1 // Default to 1 box
            };

            // Apply bulk selection if available
            if (SelectedBulkCategory != null)
            {
                newItem.CategoryId = SelectedBulkCategory.CategoryId;
                newItem.CategoryName = SelectedBulkCategory.Name;
            }

            if (SelectedBulkSupplier != null)
            {
                newItem.SupplierId = SelectedBulkSupplier.SupplierId;
                newItem.SupplierName = SelectedBulkSupplier.Name;
            }

            Items.Add(newItem);

            // Subscribe to property changes for real-time validation
            SubscribeToItemPropertyChanges(newItem);
        }

        /// <summary>
        /// Removes the specified item from the Items collection.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        private void RemoveRow(MainStockDTO item)
        {
            if (item != null)
            {
                // Unsubscribe before removing to prevent memory leaks
                UnsubscribeFromItemPropertyChanges(item);
                Items.Remove(item);
            }
        }

        /// <summary>
        /// Clears all items from the Items collection.
        /// </summary>
        private void ClearAll()
        {
            var result = MessageBox.Show("Are you sure you want to clear all items?",
                "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Unsubscribe from property changes first to prevent memory leaks
                foreach (var item in Items)
                {
                    UnsubscribeFromItemPropertyChanges(item);
                }

                Items.Clear();
                AddNewRow(); // Add one empty row
            }
        }

        /// <summary>
        /// Applies the selected bulk category to all items.
        /// </summary>
        private void ApplyBulkCategory()
        {
            if (SelectedBulkCategory == null) return;

            foreach (var item in Items)
            {
                item.CategoryId = SelectedBulkCategory.CategoryId;
                item.CategoryName = SelectedBulkCategory.Name;
            }

            StatusMessage = $"Applied category '{SelectedBulkCategory.Name}' to all items.";
        }

        /// <summary>
        /// Applies the selected bulk supplier to all items.
        /// </summary>
        private void ApplyBulkSupplier()
        {
            if (SelectedBulkSupplier == null) return;

            foreach (var item in Items)
            {
                item.SupplierId = SelectedBulkSupplier.SupplierId;
                item.SupplierName = SelectedBulkSupplier.Name;
            }

            StatusMessage = $"Applied supplier '{SelectedBulkSupplier.Name}' to all items.";
        }

        /// <summary>
        /// Applies the selected bulk invoice to all items.
        /// </summary>
        private void ApplyBulkInvoice()
        {
            if (SelectedBulkInvoice == null) return;

            Debug.WriteLine($"Applying invoice {SelectedBulkInvoice.InvoiceNumber} (ID: {SelectedBulkInvoice.SupplierInvoiceId}) to {Items.Count} items");

            foreach (var item in Items)
            {
                item.SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId;
            }

            StatusMessage = $"All items will be associated with invoice '{SelectedBulkInvoice.InvoiceNumber}' (ID: {SelectedBulkInvoice.SupplierInvoiceId}).";

            // Display confirmation to make it very clear
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"All items will be associated with invoice '{SelectedBulkInvoice.InvoiceNumber}'.\nPlease save the items to complete this association.",
                    "Invoice Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        /// <summary>
        /// Opens a dialog to add a new category and selects it for bulk application.
        /// </summary>
        private async Task AddNewCategoryAsync()
        {
            try
            {
                var dialog = new QuickCategoryDialogWindow
                {
                    Owner = GetOwnerWindow()
                };

                var result = dialog.ShowDialog();
                if (result == true && dialog.NewCategory != null)
                {
                    var newCategory = await _categoryService.CreateAsync(dialog.NewCategory);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Categories.Add(newCategory);
                        SelectedBulkCategory = newCategory;
                    });

                    StatusMessage = $"Category '{newCategory.Name}' added successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding category: {ex.Message}";
                Debug.WriteLine($"Error in AddNewCategoryAsync: {ex}");
            }
        }

        /// <summary>
        /// Opens a dialog to add a new supplier and selects it for bulk application.
        /// </summary>
        private async Task AddNewSupplierAsync()
        {
            try
            {
                var dialog = new QuickSupplierDialogWindow
                {
                    Owner = GetOwnerWindow()
                };

                var result = dialog.ShowDialog();
                if (result == true && dialog.NewSupplier != null)
                {
                    var newSupplier = await _supplierService.CreateAsync(dialog.NewSupplier);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Suppliers.Add(newSupplier);
                        SelectedBulkSupplier = newSupplier;
                    });

                    StatusMessage = $"Supplier '{newSupplier.Name}' added successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error adding supplier: {ex.Message}";
                Debug.WriteLine($"Error in AddNewSupplierAsync: {ex}");
            }
        }

        /// <summary>
        /// Opens a dialog to add a new supplier invoice and selects it for bulk application.
        /// </summary>
        private async Task AddNewInvoiceAsync()
        {
            try
            {
                // Show the quick supplier invoice dialog
                var dialog = new QuickSupplierInvoiceDialog
                {
                    Owner = GetOwnerWindow()
                };

                var result = dialog.ShowDialog();
                if (result == true && dialog.CreatedInvoice != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SupplierInvoices.Add(dialog.CreatedInvoice);
                        SelectedBulkInvoice = dialog.CreatedInvoice;
                    });

                    StatusMessage = $"Invoice '{dialog.CreatedInvoice.InvoiceNumber}' created successfully.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error creating invoice: {ex.Message}";
                Debug.WriteLine($"Error in AddNewInvoiceAsync: {ex}");
            }
        }

        /// <summary>
        /// Uploads an image for the specified item.
        /// </summary>
        /// <param name="item">The item to upload an image for.</param>
        private void UploadItemImage(MainStockDTO item)
        {
            if (item == null) return;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    Title = "Select an image for the item"
                };

                // Get the owner window properly
                var ownerWindow = GetOwnerWindow();

                if (dialog.ShowDialog(ownerWindow) == true)
                {
                    string imagePath = _imagePathService.SaveProductImage(dialog.FileName);
                    item.ImagePath = imagePath;

                    // Force UI refresh by raising property changed notification
                    if (item is INotifyPropertyChanged notifyItem)
                    {
                        // This will ensure the UI updates to show the image
                        var propertyChanged = item.GetType().GetField("PropertyChanged",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic);

                        if (propertyChanged != null)
                        {
                            var handler = propertyChanged.GetValue(item) as PropertyChangedEventHandler;
                            handler?.Invoke(item, new PropertyChangedEventArgs(nameof(item.ImagePath)));
                        }
                    }

                    StatusMessage = $"Image uploaded for item '{item.Name}'.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error uploading image: {ex.Message}";
                Debug.WriteLine($"Error in UploadItemImage: {ex}");
            }
        }

        /// <summary>
        /// Clears the image for the specified item.
        /// </summary>
        /// <param name="item">The item to clear the image for.</param>
        private void ClearItemImage(MainStockDTO item)
        {
            if (item == null || string.IsNullOrEmpty(item.ImagePath)) return;

            try
            {
                _imagePathService.DeleteProductImage(item.ImagePath);
                item.ImagePath = null;
                StatusMessage = $"Image cleared for item '{item.Name}'.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error clearing image: {ex.Message}";
                Debug.WriteLine($"Error in ClearItemImage: {ex}");
            }
        }
    }
}