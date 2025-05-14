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

                // Load categories
                var categories = await _categoryService.GetActiveAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Categories = new ObservableCollection<CategoryDTO>(categories);
                });

                // Load suppliers
                var suppliers = await _supplierService.GetActiveAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Suppliers = new ObservableCollection<SupplierDTO>(suppliers);
                });

                // Load draft supplier invoices
                var invoices = await _supplierInvoiceService.GetByStatusAsync("Draft");
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SupplierInvoices = new ObservableCollection<SupplierInvoiceDTO>(invoices);
                });

                StatusMessage = "Data loaded successfully.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading data: {ex.Message}";
                Debug.WriteLine($"Error in BulkMainStockViewModel.LoadDataAsync: {ex}");

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error loading data: {ex.Message}", "Error",
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

                // Generate missing barcodes if needed
                if (GenerateBarcodesForNewItems)
                {
                    GenerateMissingBarcodes();
                }

                IsSaving = true;
                StatusMessage = "Preparing items for processing...";

                // Fix: Process each item to ensure correct property mappings before queuing
                foreach (var item in Items)
                {
                    // Set CurrentStock based on IndividualItems + (NumberOfBoxes * ItemsPerBox)
                    item.CurrentStock = item.IndividualItems;

                    // Make sure PurchasePrice is properly set (if missing, calculate from BoxPurchasePrice)
                    if (item.PurchasePrice <= 0 && item.BoxPurchasePrice > 0 && item.ItemsPerBox > 0)
                    {
                        item.PurchasePrice = item.BoxPurchasePrice / item.ItemsPerBox;
                    }

                    // Ensure ItemsPerBox has a valid value (minimum 1)
                    if (item.ItemsPerBox <= 0)
                    {
                        item.ItemsPerBox = 1;
                    }

                    // Verify box barcode is properly set
                    if (string.IsNullOrWhiteSpace(item.BoxBarcode) && !string.IsNullOrWhiteSpace(item.Barcode))
                    {
                        item.BoxBarcode = $"BX{item.Barcode}";
                    }
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

                    statusWindow.ShowDialog();
                });

                // Set dialog result to close the window
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Store a backup in case dialog is already closed
                    DialogResultBackup = true;
                    DialogResult = true;
                });

                // Always return true since we've offloaded the actual processing to the queue service
                return true;
            }
            catch (Exception ex)
            {
                string errorMessage = ex.Message;
                var currentEx = ex;
                while (currentEx.InnerException != null)
                {
                    currentEx = currentEx.InnerException;
                    errorMessage += $"\n-> {currentEx.Message}";
                }

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
        /// Validates all items before saving.
        /// </summary>
        private (bool IsValid, List<string> ValidationErrors) ValidateItems()
        {
            var validationErrors = new List<string>();
            var invalidItems = Items.Where(i =>
                string.IsNullOrWhiteSpace(i.Name) ||
                i.CategoryId <= 0 ||
                i.SalePrice <= 0 ||
                i.ItemsPerBox <= 0).ToList();

            if (invalidItems.Any())
            {
                foreach (var item in invalidItems)
                {
                    var fieldsMessage = "Missing required fields: ";
                    var fields = new List<string>();

                    if (string.IsNullOrWhiteSpace(item.Name))
                        fields.Add("Name");

                    if (item.CategoryId <= 0)
                        fields.Add("Category");

                    if (item.SalePrice <= 0)
                        fields.Add("Sale Price");

                    if (item.ItemsPerBox <= 0)
                        fields.Add("Items per Box");

                    fieldsMessage += string.Join(", ", fields);
                    validationErrors.Add($"• {item.Name ?? "Unnamed item"}: {fieldsMessage}");
                }
                return (false, validationErrors);
            }

            return (true, validationErrors);
        }

        /// <summary>
        /// Finds existing products by barcode.
        /// </summary>
        private async Task<Dictionary<string, MainStockDTO>> FindExistingProductsAsync()
        {
            var existingProductsMap = new Dictionary<string, MainStockDTO>();
            foreach (var item in Items.Where(i => !string.IsNullOrWhiteSpace(i.Barcode)))
            {
                try
                {
                    var existingProduct = await _mainStockService.GetByBarcodeAsync(item.Barcode);
                    if (existingProduct != null)
                    {
                        existingProductsMap[item.Barcode] = existingProduct;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error looking up existing product: {ex.Message}");
                }
            }
            return existingProductsMap;
        }

        /// <summary>
        /// Updates items with data from existing products.
        /// </summary>
        private void UpdateItemsWithExistingData(Dictionary<string, MainStockDTO> existingProductsMap)
        {
            foreach (var item in Items)
            {
                // Handle case for existing products
                if (!string.IsNullOrWhiteSpace(item.Barcode) &&
                    existingProductsMap.TryGetValue(item.Barcode, out var existingProduct))
                {
                    item.MainStockId = existingProduct.MainStockId;

                    // For existing items, keep individual items and boxes separate
                    // Do NOT combine them when updating
                    item.IndividualItems = existingProduct.IndividualItems;
                    item.NumberOfBoxes = existingProduct.NumberOfBoxes + item.NumberOfBoxes;

                    // Keep any existing box-related properties that aren't being updated
                    if (string.IsNullOrWhiteSpace(item.BoxBarcode))
                    {
                        item.BoxBarcode = existingProduct.BoxBarcode;
                    }
                }
                else
                {
                    // For new items, set individual items to 0 by default
                    item.IndividualItems = 0;
                }

                // Apply consistent box barcode logic
                ApplyBoxBarcodeLogic(item);

                // Calculate prices based on box prices if needed
                CalculateItemPricesFromBoxPrices(item);

                // IMPORTANT: Do not set CurrentStock as a combination here
            }
        }

        /// <summary>
        /// Calculates item prices based on box prices if needed.
        /// </summary>
        private void CalculateItemPricesFromBoxPrices(MainStockDTO item)
        {
            // If item purchase price is zero but we have box price and items per box,
            // calculate the item purchase price
            if (item.PurchasePrice == 0 && item.BoxPurchasePrice > 0 && item.ItemsPerBox > 0)
            {
                item.PurchasePrice = item.BoxPurchasePrice / item.ItemsPerBox;
            }

            // Calculate item wholesale price if needed
            if (item.WholesalePrice == 0 && item.BoxWholesalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.WholesalePrice = item.BoxWholesalePrice / item.ItemsPerBox;
            }

            // Similarly for sale price
            if (item.SalePrice == 0 && item.BoxSalePrice > 0 && item.ItemsPerBox > 0)
            {
                item.SalePrice = item.BoxSalePrice / item.ItemsPerBox;
            }
        }

        /// <summary>
        /// Applies consistent box barcode logic to an item.
        /// </summary>
        private void ApplyBoxBarcodeLogic(MainStockDTO item)
        {
            // Case 1: Empty box barcode - apply BX prefix to item barcode
            if (string.IsNullOrWhiteSpace(item.BoxBarcode) && !string.IsNullOrWhiteSpace(item.Barcode))
            {
                item.BoxBarcode = $"BX{item.Barcode}";
            }
            // Case 2: Box barcode equals item barcode - apply BX prefix
            else if (!string.IsNullOrWhiteSpace(item.BoxBarcode) && !string.IsNullOrWhiteSpace(item.Barcode)
                     && item.BoxBarcode == item.Barcode)
            {
                item.BoxBarcode = $"BX{item.Barcode}";
            }
        }

        /// <summary>
        /// Generates missing barcodes for items that don't have them.
        /// </summary>
        private void GenerateMissingBarcodes()
        {
            foreach (var item in Items.Where(i => string.IsNullOrWhiteSpace(i.Barcode)))
            {
                var timestamp = DateTime.Now.Ticks.ToString().Substring(10, 8);
                var random = new Random();
                var randomDigits = random.Next(1000, 9999).ToString();
                var categoryPrefix = item.CategoryId.ToString().PadLeft(3, '0');

                item.Barcode = $"{categoryPrefix}{timestamp}{randomDigits}";

                // Always generate a box barcode if item barcode exists
                if (string.IsNullOrWhiteSpace(item.BoxBarcode))
                {
                    item.BoxBarcode = $"BX{item.Barcode}";
                }

                // Generate barcode image
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
        /// Associates saved items with the selected invoice.
        /// </summary>
        private async Task AssociateItemsWithInvoice(List<MainStockDTO> savedItems)
        {
            StatusMessage = $"Associating items with invoice {SelectedBulkInvoice.InvoiceNumber}...";
            int successCount = 0;
            int errorCount = 0;
            string lastErrorMessage = string.Empty;

            // Create a small delay to ensure all entities are properly saved before association
            await Task.Delay(500);

            // Process each item with careful error handling in smaller batches
            var batchSize = 10; // Process 10 items at a time

            for (int batchStart = 0; batchStart < savedItems.Count; batchStart += batchSize)
            {
                var batch = savedItems.Skip(batchStart).Take(batchSize).ToList();

                foreach (var item in batch)
                {
                    try
                    {
                        // Verify MainStockId exists and is valid
                        if (item.MainStockId <= 0)
                        {
                            Debug.WriteLine($"Invalid MainStockId for item {item.Name}");
                            errorCount++;
                            continue;
                        }

                        StatusMessage = $"Processing item {successCount + errorCount + 1} of {savedItems.Count}: {item.Name}";

                        // Get or create product - with isolation
                        ProductDTO storeProduct;

                        // First check if product already exists without tracking
                        var existingStoreProduct = await _productService.GetByBarcodeAsync(item.Barcode);

                        if (existingStoreProduct != null)
                        {
                            // Use a fresh, detached copy of the product to avoid tracking issues
                            storeProduct = new ProductDTO
                            {
                                ProductId = existingStoreProduct.ProductId,
                                Name = item.Name,
                                Barcode = item.Barcode,
                                BoxBarcode = item.BoxBarcode,
                                CategoryId = item.CategoryId,
                                CategoryName = item.CategoryName,
                                SupplierId = item.SupplierId,
                                SupplierName = item.SupplierName,
                                Description = item.Description,
                                PurchasePrice = item.PurchasePrice,
                                WholesalePrice = item.WholesalePrice,
                                SalePrice = item.SalePrice,
                                MainStockId = item.MainStockId,
                                BoxPurchasePrice = item.BoxPurchasePrice,
                                BoxWholesalePrice = item.BoxWholesalePrice,
                                BoxSalePrice = item.BoxSalePrice,
                                ItemsPerBox = item.ItemsPerBox,
                                MinimumBoxStock = item.MinimumBoxStock,
                                MinimumStock = item.MinimumStock,
                                ImagePath = item.ImagePath,
                                Speed = item.Speed,
                                IsActive = item.IsActive,
                                CurrentStock = existingStoreProduct.CurrentStock,
                                UpdatedAt = DateTime.Now,
                                // Fix: Set NumberOfBoxes and IndividualItems
                                NumberOfBoxes = item.NumberOfBoxes,
                                IndividualItems = item.IndividualItems
                            };

                            // Important: We need to actually update the product in the database
                            try
                            {
                                await _productService.UpdateAsync(storeProduct);
                                Debug.WriteLine($"Updated existing product {storeProduct.ProductId} with wholesale prices: Item={storeProduct.WholesalePrice}, Box={storeProduct.BoxWholesalePrice}");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error updating existing product with wholesale prices: {ex.Message}");
                                // Continue despite error - we don't want to fail the whole operation
                            }
                        }
                        else
                        {
                            // Create a fresh product object
                            storeProduct = new ProductDTO
                            {
                                Name = item.Name,
                                Barcode = item.Barcode,
                                BoxBarcode = item.BoxBarcode,
                                CategoryId = item.CategoryId,
                                CategoryName = item.CategoryName,
                                SupplierId = item.SupplierId,
                                SupplierName = item.SupplierName,
                                Description = item.Description,
                                PurchasePrice = item.PurchasePrice, // Fix: Ensure PurchasePrice is set 
                                WholesalePrice = item.WholesalePrice,
                                SalePrice = item.SalePrice,
                                MainStockId = item.MainStockId,
                                BoxPurchasePrice = item.BoxPurchasePrice,
                                BoxSalePrice = item.BoxSalePrice,
                                BoxWholesalePrice = item.BoxWholesalePrice,
                                ItemsPerBox = item.ItemsPerBox > 0 ? item.ItemsPerBox : 1, // Fix: Ensure ItemsPerBox is valid
                                MinimumBoxStock = item.MinimumBoxStock,
                                CurrentStock = (int)item.CurrentStock, // Fix: Set CurrentStock from calculated value // Fix: Set CurrentStock from calculated value
                                MinimumStock = item.MinimumStock,
                                ImagePath = item.ImagePath,
                                Speed = item.Speed,
                                IsActive = item.IsActive,
                                CreatedAt = DateTime.Now,
                                // Fix: Set NumberOfBoxes and IndividualItems
                                NumberOfBoxes = (int)item.NumberOfBoxes,
                                IndividualItems = (int)item.IndividualItems
                            };

                            try
                            {
                                storeProduct = await _productService.CreateAsync(storeProduct);
                            }
                            catch (Exception ex)
                            {
                                throw new Exception($"Failed to create product: {ex.Message}", ex);
                            }
                        }

                        // Wait a moment to let any db operations complete
                        await Task.Delay(200);

                        // Now create the invoice detail - but with key focus on avoiding tracking conflicts
                        try
                        {
                            // Prepare the invoice detail object
                            var invoiceDetail = new SupplierInvoiceDetailDTO
                            {
                                SupplierInvoiceId = SelectedBulkInvoice.SupplierInvoiceId,
                                ProductId = storeProduct.ProductId,
                                ProductName = item.Name,
                                ProductBarcode = item.Barcode,
                                BoxBarcode = item.BoxBarcode,
                                NumberOfBoxes = item.NumberOfBoxes, // Fix: Ensure NumberOfBoxes is used
                                ItemsPerBox = item.ItemsPerBox > 0 ? item.ItemsPerBox : 1, // Fix: Ensure valid ItemsPerBox
                                BoxPurchasePrice = item.BoxPurchasePrice,
                                BoxSalePrice = item.BoxSalePrice,
                                Quantity = item.CurrentStock, // Fix: Use CurrentStock as calculated earlier
                                PurchasePrice = item.PurchasePrice, // Fix: Ensure PurchasePrice is set
                                TotalPrice = item.PurchasePrice * item.CurrentStock // Fix: Calculate correct total price
                            };

                            // Process invoice detail in a completely separate operation
                            StatusMessage = $"Adding product to invoice: {item.Name}";
                            await _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail);
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to add product to invoice: {ex.Message}");
                            throw new Exception($"Failed to add product to invoice: {ex.Message}", ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        lastErrorMessage = ex.Message;
                        Debug.WriteLine($"Error associating item with invoice: {ex}");
                    }
                }

                // Add a delay between batches to avoid overwhelming the database
                await Task.Delay(300);
            }

            // Show a message with the results
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (successCount > 0)
                {
                    string message = $"Successfully added {successCount} products to invoice '{SelectedBulkInvoice.InvoiceNumber}'.";
                    if (errorCount > 0)
                    {
                        message += $"\n\n{errorCount} items couldn't be added. Last error: {lastErrorMessage}";
                    }

                    MessageBox.Show(message, "Invoice Association",
                        MessageBoxButton.OK, errorCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
                else if (errorCount > 0)
                {
                    MessageBox.Show($"Failed to add any products to the invoice. Error: {lastErrorMessage}",
                        "Invoice Association Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        /// <summary>
        /// Looks up a product by barcode.
        /// </summary>
        /// <param name="currentItem">The item to populate with product data.</param>
        private async Task LookupProductAsync(MainStockDTO currentItem)
        {
            if (currentItem == null || string.IsNullOrWhiteSpace(currentItem.Barcode))
                return;

            try
            {
                StatusMessage = "Searching for product...";
                IsSaving = true;

                // Look for existing product by barcode using a fresh DbContext
                var existingProduct = await _mainStockService.GetByBarcodeAsync(currentItem.Barcode);

                // If found, populate the current item with existing data
                if (existingProduct != null)
                {
                    // Keep the current box quantities
                    int numberOfBoxes = currentItem.NumberOfBoxes > 0 ? currentItem.NumberOfBoxes : 1;

                    // Copy all properties from existing product
                    currentItem.MainStockId = existingProduct.MainStockId;
                    currentItem.Name = existingProduct.Name;
                    currentItem.Description = existingProduct.Description;
                    currentItem.CategoryId = existingProduct.CategoryId;
                    currentItem.CategoryName = existingProduct.CategoryName;
                    currentItem.SupplierId = existingProduct.SupplierId;
                    currentItem.SupplierName = existingProduct.SupplierName;
                    currentItem.PurchasePrice = existingProduct.PurchasePrice;
                    currentItem.SalePrice = existingProduct.SalePrice;
                    currentItem.WholesalePrice = existingProduct.WholesalePrice;
                    currentItem.BoxBarcode = existingProduct.BoxBarcode;
                    currentItem.BoxPurchasePrice = existingProduct.BoxPurchasePrice;
                    currentItem.BoxSalePrice = existingProduct.BoxSalePrice;
                    currentItem.BoxWholesalePrice = existingProduct.BoxWholesalePrice;
                    currentItem.ItemsPerBox = existingProduct.ItemsPerBox > 0 ? existingProduct.ItemsPerBox : 1;
                    currentItem.MinimumStock = existingProduct.MinimumStock;
                    currentItem.MinimumBoxStock = existingProduct.MinimumBoxStock;
                    currentItem.Speed = existingProduct.Speed;
                    currentItem.IsActive = existingProduct.IsActive;
                    currentItem.ImagePath = existingProduct.ImagePath;

                    // Restore the number of boxes as this is likely what the user is adding
                    currentItem.NumberOfBoxes = numberOfBoxes;

                    // Generate barcode image if needed
                    if (currentItem.BarcodeImage == null)
                    {
                        try
                        {
                            currentItem.BarcodeImage = _barcodeService.GenerateBarcode(currentItem.Barcode);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                        }
                    }

                    StatusMessage = $"Found existing product: {currentItem.Name}";
                }
                else
                {
                    // Check if box barcode field is empty or the same as item barcode
                    if (!string.IsNullOrWhiteSpace(currentItem.BoxBarcode) &&
                        currentItem.BoxBarcode == currentItem.Barcode)
                    {
                        // Auto-prefix the box barcode with "BX" only when there's an exact match
                        currentItem.BoxBarcode = $"BX{currentItem.Barcode}";
                    }

                    StatusMessage = "New product. Please enter details.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error looking up product: {ex.Message}";
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// Looks up a product by box barcode.
        /// </summary>
        /// <param name="currentItem">The item to populate with product data.</param>
        private async Task LookupBoxBarcodeAsync(MainStockDTO currentItem)
        {
            if (currentItem == null || string.IsNullOrWhiteSpace(currentItem.BoxBarcode))
                return;

            try
            {
                StatusMessage = "Searching for product by box barcode...";
                IsSaving = true;

                // Look for existing product by box barcode
                var existingProduct = await _mainStockService.GetByBoxBarcodeAsync(currentItem.BoxBarcode);

                if (existingProduct != null)
                {
                    // Keep the current box quantities
                    int numberOfBoxes = currentItem.NumberOfBoxes > 0 ? currentItem.NumberOfBoxes : 1;

                    // Copy properties from existing product
                    currentItem.MainStockId = existingProduct.MainStockId;
                    currentItem.Name = existingProduct.Name;
                    currentItem.Barcode = existingProduct.Barcode;
                    currentItem.Description = existingProduct.Description;
                    currentItem.CategoryId = existingProduct.CategoryId;
                    currentItem.CategoryName = existingProduct.CategoryName;
                    currentItem.SupplierId = existingProduct.SupplierId;
                    currentItem.SupplierName = existingProduct.SupplierName;
                    currentItem.PurchasePrice = existingProduct.PurchasePrice;
                    currentItem.WholesalePrice = existingProduct.WholesalePrice;
                    currentItem.SalePrice = existingProduct.SalePrice;
                    currentItem.BoxPurchasePrice = existingProduct.BoxPurchasePrice;
                    currentItem.BoxWholesalePrice = existingProduct.BoxWholesalePrice;
                    currentItem.BoxSalePrice = existingProduct.BoxSalePrice;
                    currentItem.ItemsPerBox = existingProduct.ItemsPerBox > 0 ? existingProduct.ItemsPerBox : 1;
                    currentItem.MinimumStock = existingProduct.MinimumStock;
                    currentItem.MinimumBoxStock = existingProduct.MinimumBoxStock;
                    currentItem.Speed = existingProduct.Speed;
                    currentItem.IsActive = existingProduct.IsActive;
                    currentItem.ImagePath = existingProduct.ImagePath;

                    // Restore the number of boxes
                    currentItem.NumberOfBoxes = numberOfBoxes;

                    // Generate barcode image if needed
                    if (currentItem.BarcodeImage == null && !string.IsNullOrWhiteSpace(currentItem.Barcode))
                    {
                        try
                        {
                            currentItem.BarcodeImage = _barcodeService.GenerateBarcode(currentItem.Barcode);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating barcode image: {ex.Message}");
                        }
                    }

                    StatusMessage = $"Found existing product by box barcode: {currentItem.Name}";
                }
                else
                {
                    StatusMessage = "No product found with this box barcode.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error looking up box barcode: {ex.Message}";
                Debug.WriteLine($"Error in LookupBoxBarcodeAsync: {ex}");
            }
            finally
            {
                IsSaving = false;
            }
        }
    }
}