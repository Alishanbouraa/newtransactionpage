// Path: QuickTechSystems.WPF.ViewModels/BulkMainStockViewModel.DataOperations.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.WPF.Views;

namespace QuickTechSystems.WPF.ViewModels
{
    public partial class BulkMainStockViewModel
    {
        /// <summary>
        /// Loads reference data for the view.
        /// </summary>
        private async Task LoadDataAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                // Load categories with proper error handling
                try
                {
                    var categories = await _categoryService.GetActiveAsync();
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Categories = new ObservableCollection<CategoryDTO>(categories);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading categories: {ex.Message}");
                    ShowTemporaryErrorMessage("Error loading categories. Functionality may be limited.");
                }

                // Load suppliers with proper error handling
                try
                {
                    var suppliers = await _supplierService.GetActiveAsync();
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading suppliers: {ex.Message}");
                    ShowTemporaryErrorMessage("Error loading suppliers. Functionality may be limited.");
                }

                // Load draft supplier invoices with proper error handling
                try
                {
                    var invoices = await _supplierInvoiceService.GetByStatusAsync("Draft");
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>(invoices);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading invoices: {ex.Message}");
                    ShowTemporaryErrorMessage("Error loading invoices. Functionality may be limited.");
                }

                StatusMessage = "Data loaded successfully.";
            }
            catch (Exception ex)
            {
                var errorMessage = GetDetailedErrorMessage(ex);
                StatusMessage = $"Error loading data: {errorMessage}";
                Debug.WriteLine($"Error in BulkMainStockViewModel.LoadDataAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error loading data: {errorMessage}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// Saves all items to the database with improved concurrency handling.
        /// </summary>
        public async Task<bool> SaveAllAsync()
        {
            try
            {
                // Validate items before attempting to save
                var validationResult = ValidateItems();
                if (!validationResult.IsValid)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Please fix the following issues before saving:\n\n{string.Join("\n", validationResult.ValidationErrors)}",
                            "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });

                    return false;
                }

                // Validate invoice selection for bulk operation
                if (SelectedBulkInvoice == null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show("Please select a supplier invoice before saving items.",
                            "Invoice Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });

                    return false;
                }

                // Generate missing barcodes if needed
                if (GenerateBarcodesForNewItems)
                {
                    GenerateMissingBarcodes();
                }

                IsSaving = true;
                StatusMessage = "Preparing items for processing...";

                // Pre-process each item to ensure correct properties and data integrity
                foreach (var item in Items)
                {
                    // Set CurrentStock based on IndividualItems
                    item.CurrentStock = item.IndividualItems;

                    // Make sure prices are properly set
                    EnsureConsistentPricing(item);

                    // Ensure ItemsPerBox has a valid value (minimum 1)
                    if (item.ItemsPerBox <= 0)
                    {
                        item.ItemsPerBox = 0;
                    }

                    // Verify box barcode is properly set
                    if (string.IsNullOrWhiteSpace(item.BoxBarcode) && !string.IsNullOrWhiteSpace(item.Barcode))
                    {
                        item.BoxBarcode = $"BX{item.Barcode}";
                    }

                    // Make sure timestamps are properly set
                    if (item.CreatedAt == default)
                    {
                        item.CreatedAt = DateTime.Now;
                    }

                    item.UpdatedAt = DateTime.Now;

                    // Set supplier invoice ID to the selected bulk invoice
                    item.SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId;
                }

                // Add all items to the queue
                _bulkOperationQueueService.EnqueueItems(Items.ToList());

                // Show the status window
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var statusViewModel = new BulkProcessingStatusViewModel(_bulkOperationQueueService, _eventAggregator);
                    var statusWindow = new BulkProcessingStatusWindow
                    {
                        Owner = GetOwnerWindow(),
                        DataContext = statusViewModel
                    };

                    statusViewModel.CloseRequested += (sender, args) =>
                    {
                        statusWindow.DialogResult = args.DialogResult;
                    };

                    // Handle window closing
                    var result = statusWindow.ShowDialog();

                    // After processing is complete, handle supplier invoice integration
                    if (result == true)
                    {
                        ProcessSupplierInvoiceIntegration();
                    }

                    // Set dialog result to close the window
                    DialogResultBackup = result == true;
                    DialogResult = result;
                });

                return true;
            }
            catch (Exception ex)
            {
                string errorMessage = GetDetailedErrorMessage(ex);
                StatusMessage = $"Error preparing items: {errorMessage}";
                Debug.WriteLine($"Error in SaveAllAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error preparing items: {errorMessage}",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });

                return false;
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// Processes supplier invoice integration after the batch operation is complete
        /// </summary>
        private async void ProcessSupplierInvoiceIntegration()
        {
            try
            {
                if (SelectedBulkInvoice == null)
                {
                    Debug.WriteLine("No invoice selected for integration");
                    return;
                }

                StatusMessage = "Integrating with supplier invoice...";
                IsSaving = true;

                // Get the saved products from the database to ensure we have valid IDs
                var savedItems = new List<MainStockDTO>();
                foreach (var item in Items)
                {
                    if (!string.IsNullOrWhiteSpace(item.Barcode))
                    {
                        var savedItem = await _mainStockService.GetByBarcodeAsync(item.Barcode);
                        if (savedItem != null)
                        {
                            savedItems.Add(savedItem);
                        }
                    }
                }

                int processedCount = 0;
                foreach (var mainStockItem in savedItems)
                {
                    try
                    {
                        // First, find or create the store product
                        var existingProduct = await _productService.FindProductByBarcodeAsync(mainStockItem.Barcode);
                        ProductDTO storeProduct;

                        if (existingProduct != null)
                        {
                            // Update existing product
                            storeProduct = new ProductDTO
                            {
                                ProductId = existingProduct.ProductId,
                                Name = mainStockItem.Name,
                                Barcode = mainStockItem.Barcode,
                                BoxBarcode = mainStockItem.BoxBarcode,
                                CategoryId = mainStockItem.CategoryId,
                                CategoryName = mainStockItem.CategoryName,
                                SupplierId = mainStockItem.SupplierId,
                                SupplierName = mainStockItem.SupplierName,
                                Description = mainStockItem.Description,
                                PurchasePrice = mainStockItem.PurchasePrice,
                                WholesalePrice = mainStockItem.WholesalePrice,
                                SalePrice = mainStockItem.SalePrice,
                                MainStockId = mainStockItem.MainStockId,
                                BoxPurchasePrice = mainStockItem.BoxPurchasePrice,
                                BoxWholesalePrice = mainStockItem.BoxWholesalePrice,
                                BoxSalePrice = mainStockItem.BoxSalePrice,
                                ItemsPerBox = mainStockItem.ItemsPerBox,
                                MinimumBoxStock = mainStockItem.MinimumBoxStock,
                                MinimumStock = mainStockItem.MinimumStock,
                                ImagePath = mainStockItem.ImagePath,
                                Speed = mainStockItem.Speed,
                                IsActive = mainStockItem.IsActive,
                                CurrentStock = existingProduct.CurrentStock,
                                UpdatedAt = DateTime.Now
                            };
                            await _productService.UpdateAsync(storeProduct);
                        }
                        else
                        {
                            // Create new product
                            storeProduct = new ProductDTO
                            {
                                Name = mainStockItem.Name,
                                Barcode = mainStockItem.Barcode,
                                BoxBarcode = mainStockItem.BoxBarcode,
                                CategoryId = mainStockItem.CategoryId,
                                CategoryName = mainStockItem.CategoryName,
                                SupplierId = mainStockItem.SupplierId,
                                SupplierName = mainStockItem.SupplierName,
                                Description = mainStockItem.Description,
                                PurchasePrice = mainStockItem.PurchasePrice,
                                WholesalePrice = mainStockItem.WholesalePrice,
                                SalePrice = mainStockItem.SalePrice,
                                MainStockId = mainStockItem.MainStockId,
                                BoxPurchasePrice = mainStockItem.BoxPurchasePrice,
                                BoxWholesalePrice = mainStockItem.BoxWholesalePrice,
                                BoxSalePrice = mainStockItem.BoxSalePrice,
                                ItemsPerBox = mainStockItem.ItemsPerBox,
                                MinimumBoxStock = mainStockItem.MinimumBoxStock,
                                CurrentStock = 0,
                                MinimumStock = mainStockItem.MinimumStock,
                                ImagePath = mainStockItem.ImagePath,
                                Speed = mainStockItem.Speed,
                                IsActive = mainStockItem.IsActive,
                                CreatedAt = DateTime.Now
                            };
                            storeProduct = await _productService.CreateAsync(storeProduct);
                        }

                        // Then, create the supplier invoice detail
                        var invoiceDetail = new SupplierInvoiceDetailDTO
                        {
                            SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId,
                            ProductId = storeProduct.ProductId,
                            ProductName = mainStockItem.Name,
                            ProductBarcode = mainStockItem.Barcode,
                            BoxBarcode = mainStockItem.BoxBarcode,
                            NumberOfBoxes = mainStockItem.NumberOfBoxes,
                            ItemsPerBox = mainStockItem.ItemsPerBox,
                            BoxPurchasePrice = mainStockItem.BoxPurchasePrice,
                            BoxSalePrice = mainStockItem.BoxSalePrice,
                            Quantity = mainStockItem.CurrentStock,
                            PurchasePrice = mainStockItem.PurchasePrice,
                            TotalPrice = mainStockItem.PurchasePrice * mainStockItem.CurrentStock
                        };

                        await _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail);
                        processedCount++;

                        // Update status periodically
                        if (processedCount % 5 == 0)
                        {
                            StatusMessage = $"Processed {processedCount} items for invoice...";
                            await Task.Delay(10); // Allow UI to update
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing invoice integration for item {mainStockItem.Name}: {ex.Message}");
                        // Continue with next item despite error
                    }
                }

                StatusMessage = $"Successfully integrated {processedCount} items with invoice {SelectedBulkInvoice.InvoiceNumber}";
                await Task.Delay(2000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in supplier invoice integration: {ex.Message}");
                StatusMessage = $"Error integrating with invoice: {ex.Message}";
                await Task.Delay(3000);
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
            }
        }

        /// <summary>
        /// Ensures consistent pricing for the item, respecting manually entered values
        /// </summary>
        private void EnsureConsistentPricing(MainStockDTO item)
        {
            // If user has entered both Box Purchase Price and Purchase Price directly
            // We don't adjust either one - respect both user inputs
            if (item.BoxPurchasePrice > 0 && item.PurchasePrice > 0)
            {
                // Do nothing - respect both values as the user entered them
            }
            // Calculate purchase price from box price if needed
            else if (item.PurchasePrice <= 0 && item.BoxPurchasePrice > 0 && item.ItemsPerBox > 0)
            {
                item.PurchasePrice = Math.Round(item.BoxPurchasePrice / item.ItemsPerBox, 2);
            }
            // Calculate box purchase price from item price if needed (only if ItemsPerBox is set)
            else if (item.BoxPurchasePrice <= 0 && item.PurchasePrice > 0 && item.ItemsPerBox > 0)
            {
                item.BoxPurchasePrice = Math.Round(item.PurchasePrice * item.ItemsPerBox, 2);
            }

            // Handle Wholesale Price similarly - only if ItemsPerBox > 0
            if (item.BoxWholesalePrice > 0 && item.WholesalePrice > 0)
            {
                // Respect both user inputs
            }
            // Calculate wholesale price from box wholesale price if needed
            else if (item.WholesalePrice <= 0 && item.BoxWholesalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.WholesalePrice = Math.Round(item.BoxWholesalePrice / item.ItemsPerBox, 2);
            }
            // Calculate box wholesale price from item wholesale price if needed (only if ItemsPerBox is set)
            else if (item.BoxWholesalePrice <= 0 && item.WholesalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.BoxWholesalePrice = Math.Round(item.WholesalePrice * item.ItemsPerBox, 2);
            }

            // Handle Sale Price similarly - only if ItemsPerBox > 0
            if (item.BoxSalePrice > 0 && item.SalePrice > 0)
            {
                // Respect both user inputs
            }
            // Calculate sale price from box sale price if needed
            else if (item.SalePrice <= 0 && item.BoxSalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.SalePrice = Math.Round(item.BoxSalePrice / item.ItemsPerBox, 2);
            }
            // Calculate box sale price from item sale price if needed (only if ItemsPerBox is set)
            else if (item.BoxSalePrice <= 0 && item.SalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.BoxSalePrice = Math.Round(item.SalePrice * item.ItemsPerBox, 2);
            }

            // Ensure we have a valid wholesale price (default to purchase price + 10% if not set)
            if (item.WholesalePrice <= 0 && item.PurchasePrice > 0)
            {
                item.WholesalePrice = Math.Round(item.PurchasePrice * 1.1m, 2);
            }

            // Ensure we have a valid sale price (default to purchase price + 20% if not set)
            if (item.SalePrice <= 0 && item.PurchasePrice > 0)
            {
                item.SalePrice = Math.Round(item.PurchasePrice * 1.2m, 2);
            }

            // Only set box prices from item prices if ItemsPerBox is valid
            if (item.ItemsPerBox > 0)
            {
                // Ensure we have a valid box wholesale price (default to wholesale price * ItemsPerBox if not set)
                if (item.BoxWholesalePrice <= 0 && item.WholesalePrice > 0)
                {
                    item.BoxWholesalePrice = Math.Round(item.WholesalePrice * item.ItemsPerBox, 2);
                }

                // Ensure we have a valid box sale price (default to sale price * ItemsPerBox if not set)
                if (item.BoxSalePrice <= 0 && item.SalePrice > 0)
                {
                    item.BoxSalePrice = Math.Round(item.SalePrice * item.ItemsPerBox, 2);
                }
            }
        }

        /// <summary>
        /// Validates all items before saving.
        /// </summary>
        private (bool IsValid, List<string> ValidationErrors) ValidateItems()
        {
            var validationErrors = new List<string>();

            // Check for empty collection first
            if (Items.Count == 0)
            {
                validationErrors.Add("No items to save.");
                return (false, validationErrors);
            }

            // Check for invalid items
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                var itemErrors = new List<string>();

                // Check for required fields
                if (string.IsNullOrWhiteSpace(item.Name))
                    itemErrors.Add("Name is required");

                if (item.CategoryId <= 0)
                    itemErrors.Add("Category is required");

                if (!item.SupplierId.HasValue || item.SupplierId <= 0)
                    itemErrors.Add("Supplier is required");

                // At least one price option must be provided
                if (item.PurchasePrice <= 0 && item.BoxPurchasePrice <= 0)
                    itemErrors.Add("Either item purchase price or box purchase price is required");

                if (item.SalePrice <= 0 && item.BoxSalePrice <= 0)
                    itemErrors.Add("Either item sale price or box sale price is required");

                // Individual items quantity is required
                if (item.IndividualItems <= 0)
                    itemErrors.Add("Individual items quantity must be greater than zero");

                // Add errors for this item if any
                if (itemErrors.Count > 0)
                {
                    validationErrors.Add($"Item {i + 1} ({item.Name ?? "Unnamed"}): {string.Join(", ", itemErrors)}");
                }
            }

            // Check for duplicate barcodes
            var barcodes = new Dictionary<string, int>();
            var boxBarcodes = new Dictionary<string, int>();

            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];

                // Check item barcode for duplicates
                if (!string.IsNullOrWhiteSpace(item.Barcode))
                {
                    if (barcodes.TryGetValue(item.Barcode, out var index))
                    {
                        validationErrors.Add($"Duplicate barcode '{item.Barcode}' found in items {index + 1} and {i + 1}");
                    }
                    else
                    {
                        barcodes[item.Barcode] = i;
                    }
                }

                // Check box barcode for duplicates (only if provided)
                if (!string.IsNullOrWhiteSpace(item.BoxBarcode))
                {
                    if (boxBarcodes.TryGetValue(item.BoxBarcode, out var index))
                    {
                        validationErrors.Add($"Duplicate box barcode '{item.BoxBarcode}' found in items {index + 1} and {i + 1}");
                    }
                    else
                    {
                        boxBarcodes[item.BoxBarcode] = i;
                    }
                }

                // Check if box barcode equals item barcode
                if (!string.IsNullOrWhiteSpace(item.Barcode) &&
                    !string.IsNullOrWhiteSpace(item.BoxBarcode) &&
                    item.Barcode == item.BoxBarcode)
                {
                    validationErrors.Add($"Item {i + 1} ({item.Name}): Box barcode must be different from item barcode");
                }
            }

            return (validationErrors.Count == 0, validationErrors);
        }

        /// <summary>
        /// Generates missing barcodes for items that don't have them.
        /// </summary>
        private void GenerateMissingBarcodes()
        {
            // Get current time for consistent timestamp across all generated barcodes
            var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
            var random = new Random();

            foreach (var item in Items.Where(i => string.IsNullOrWhiteSpace(i.Barcode)))
            {
                // Generate a unique barcode with better uniqueness guarantees
                var randomDigits = random.Next(1000, 9999).ToString();
                var categoryPrefix = item.CategoryId > 0 ? item.CategoryId.ToString().PadLeft(3, '0') : "000";

                // Add a checksum digit to improve barcode integrity
                var baseCode = $"{categoryPrefix}{timestamp}{randomDigits}";

                // Simple checksum: sum of all digits modulo 10
                int sum = 0;
                foreach (char c in baseCode)
                {
                    if (char.IsDigit(c))
                    {
                        sum += (c - '0');
                    }
                }
                int checkDigit = sum % 10;

                item.Barcode = $"{baseCode}{checkDigit}";

                // Always generate a box barcode if item barcode exists
                if (string.IsNullOrWhiteSpace(item.BoxBarcode))
                {
                    item.BoxBarcode = $"BX{item.Barcode}";
                }

                // Generate barcode image if possible
                try
                {
                    item.BarcodeImage = _barcodeService.GenerateBarcode(item.Barcode);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                    // Continue despite error
                }
            }
        }

        /// <summary>
        /// Gets a detailed error message from an exception
        /// </summary>
        /// <param name="ex">The exception</param>
        /// <returns>A detailed error message</returns>
        private string GetDetailedErrorMessage(Exception ex)
        {
            if (ex == null)
                return "Unknown error";

            var message = ex.Message;
            var currentEx = ex;

            while (currentEx.InnerException != null)
            {
                currentEx = currentEx.InnerException;
                message = currentEx.Message;
            }

            return message;
        }
    }
}