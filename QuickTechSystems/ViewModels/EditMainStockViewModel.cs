// Path: QuickTechSystems.WPF.ViewModels/EditMainStockViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.WPF.ViewModels
{
    public class EditMainStockViewModel : ViewModelBase
    {
        #region Services

        private readonly IMainStockService _mainStockService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly ISupplierInvoiceService _supplierInvoiceService;
        private readonly IBarcodeService _barcodeService;
        private readonly IImagePathService _imagePathService;
        private readonly IProductService _productService;

        #endregion

        #region Properties

        private MainStockDTO _editingItem;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private ObservableCollection<SupplierInvoiceDTO> _draftInvoices;
        private CategoryDTO _selectedCategory;
        private SupplierDTO _selectedSupplier;
        private SupplierInvoiceDTO _selectedInvoice;
        private bool _isSaving;
        private string _statusMessage;
        private bool? _dialogResult;

        public MainStockDTO EditingItem
        {
            get => _editingItem;
            set => SetProperty(ref _editingItem, value);
        }

        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public ObservableCollection<SupplierDTO> Suppliers
        {
            get => _suppliers;
            set => SetProperty(ref _suppliers, value);
        }

        public ObservableCollection<SupplierInvoiceDTO> DraftInvoices
        {
            get => _draftInvoices;
            set => SetProperty(ref _draftInvoices, value);
        }

        public CategoryDTO SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value) && value != null && EditingItem != null)
                {
                    EditingItem.CategoryId = value.CategoryId;
                    EditingItem.CategoryName = value.Name;
                }
            }
        }

        public SupplierDTO SelectedSupplier
        {
            get => _selectedSupplier;
            set
            {
                if (SetProperty(ref _selectedSupplier, value) && value != null && EditingItem != null)
                {
                    EditingItem.SupplierId = value.SupplierId;
                    EditingItem.SupplierName = value.Name;
                }
            }
        }

        public SupplierInvoiceDTO SelectedInvoice
        {
            get => _selectedInvoice;
            set
            {
                if (SetProperty(ref _selectedInvoice, value) && EditingItem != null)
                {
                    EditingItem.SupplierInvoiceId = value?.SupplierInvoiceId;
                }
            }
        }

        public bool IsSaving
        {
            get => _isSaving;
            set => SetProperty(ref _isSaving, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool? DialogResult
        {
            get => _dialogResult;
            set => SetProperty(ref _dialogResult, value);
        }

        #endregion

        #region Commands

        public ICommand SaveCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand GenerateBarcodeCommand { get; private set; }
        public ICommand UploadImageCommand { get; private set; }
        public ICommand ClearImageCommand { get; private set; }
        public ICommand AddNewCategoryCommand { get; private set; }
        public ICommand AddNewSupplierCommand { get; private set; }
        public ICommand ClearInvoiceCommand { get; private set; }
        public ICommand LookupProductCommand { get; private set; }
        public ICommand LookupBoxBarcodeCommand { get; private set; }

        #endregion

        public EditMainStockViewModel(
            IMainStockService mainStockService,
            ICategoryService categoryService,
            ISupplierService supplierService,
            ISupplierInvoiceService supplierInvoiceService,
            IBarcodeService barcodeService,
            IImagePathService imagePathService,
            IProductService productService,
            IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _mainStockService = mainStockService;
            _categoryService = categoryService;
            _supplierService = supplierService;
            _supplierInvoiceService = supplierInvoiceService;
            _barcodeService = barcodeService;
            _imagePathService = imagePathService;
            _productService = productService;

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync());
            CancelCommand = new RelayCommand(_ => Cancel());
            GenerateBarcodeCommand = new RelayCommand(_ => GenerateBarcode());
            UploadImageCommand = new RelayCommand(_ => UploadImage());
            ClearImageCommand = new RelayCommand(_ => ClearImage());
            AddNewCategoryCommand = new AsyncRelayCommand(async _ => await AddNewCategoryAsync());
            AddNewSupplierCommand = new AsyncRelayCommand(async _ => await AddNewSupplierAsync());
            ClearInvoiceCommand = new RelayCommand(_ => ClearInvoice());
            LookupProductCommand = new AsyncRelayCommand<MainStockDTO>(async item => await LookupProductAsync(item));
            LookupBoxBarcodeCommand = new AsyncRelayCommand<MainStockDTO>(async item => await LookupBoxBarcodeAsync(item));
        }

        public async Task InitializeAsync(MainStockDTO item)
        {
            EditingItem = item ?? new MainStockDTO { IsActive = true };

            try
            {
                IsSaving = true;
                StatusMessage = "Loading data...";

                await LoadDataAsync();

                // Set selected values from the editing item
                if (EditingItem != null)
                {
                    if (EditingItem.CategoryId > 0)
                    {
                        SelectedCategory = Categories?.FirstOrDefault(c => c.CategoryId == EditingItem.CategoryId);
                    }

                    if (EditingItem.SupplierId.HasValue && EditingItem.SupplierId.Value > 0)
                    {
                        SelectedSupplier = Suppliers?.FirstOrDefault(s => s.SupplierId == EditingItem.SupplierId.Value);
                    }

                    // Enhanced invoice selection logic
                    if (EditingItem.SupplierInvoiceId.HasValue && EditingItem.SupplierInvoiceId.Value > 0)
                    {
                        Debug.WriteLine($"Looking for invoice ID: {EditingItem.SupplierInvoiceId.Value}");

                        // First check if the invoice is already loaded in DraftInvoices
                        var existingInvoice = DraftInvoices?.FirstOrDefault(i =>
                            i.SupplierInvoiceId == EditingItem.SupplierInvoiceId.Value);

                        if (existingInvoice != null)
                        {
                            Debug.WriteLine($"Found existing invoice in collection: {existingInvoice.InvoiceNumber}");
                            SelectedInvoice = existingInvoice;
                        }
                        else
                        {
                            // If not found in the loaded invoices, try to fetch it specifically
                            try
                            {
                                Debug.WriteLine("Invoice not found in collection, fetching from database...");
                                var invoice = await _supplierInvoiceService.GetByIdAsync(EditingItem.SupplierInvoiceId.Value);

                                if (invoice != null)
                                {
                                    Debug.WriteLine($"Successfully loaded invoice from database: {invoice.InvoiceNumber}");

                                    // Add to collection if not already there
                                    DraftInvoices.Add(invoice);

                                    // Set as selected invoice (AFTER adding to collection)
                                    SelectedInvoice = invoice;

                                    // Force UI update to ensure selection appears
                                    OnPropertyChanged(nameof(SelectedInvoice));
                                    OnPropertyChanged(nameof(DraftInvoices));
                                }
                                else
                                {
                                    Debug.WriteLine("No invoice found in database with that ID");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error loading invoice: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine("No SupplierInvoiceId found on the editing item");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing edit dialog: {ex.Message}");
                StatusMessage = $"Error loading data: {ex.Message}";
            }
            finally
            {
                IsSaving = false;
                StatusMessage = string.Empty;
            }
        }

        protected override async Task LoadDataAsync()
        {
            try
            {
                // Load categories
                var categories = await _categoryService.GetActiveAsync();
                Categories = new ObservableCollection<CategoryDTO>(categories);

                // Load suppliers
                var suppliers = await _supplierService.GetActiveAsync();
                Suppliers = new ObservableCollection<SupplierDTO>(suppliers);

                // Load invoices with enhanced approach
                var invoices = new List<SupplierInvoiceDTO>();

                // Get draft invoices (for new items)
                var draftInvoices = await _supplierInvoiceService.GetByStatusAsync("Draft");
                invoices.AddRange(draftInvoices);

                // Also load recent invoices of all statuses (for editing existing items)
                var recentDate = DateTime.Now.AddDays(-90);  // Go back 90 days to ensure we find most invoices
                var recentInvoices = await _supplierInvoiceService.GetRecentInvoicesAsync(recentDate);

                // Add recent invoices without duplicating
                foreach (var invoice in recentInvoices)
                {
                    if (!invoices.Any(i => i.SupplierInvoiceId == invoice.SupplierInvoiceId))
                    {
                        invoices.Add(invoice);
                    }
                }

                // If we're editing an item with a specific invoice ID that's not in either collection,
                // we'll fetch it separately in the InitializeAsync method

                DraftInvoices = new ObservableCollection<SupplierInvoiceDTO>(invoices);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading data: {ex.Message}");
                throw;
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Saving changes...";

                if (EditingItem == null)
                {
                    StatusMessage = "No item to save";
                    return;
                }

                // Ensure data integrity
                if (string.IsNullOrWhiteSpace(EditingItem.BoxBarcode) && !string.IsNullOrWhiteSpace(EditingItem.Barcode))
                    EditingItem.BoxBarcode = $"BX{EditingItem.Barcode}";

                if (EditingItem.ItemsPerBox <= 0)
                    EditingItem.ItemsPerBox = 1;


                // Validate the item
                if (!ValidateItem())
                    return;

                // Ensure the invoice ID is set on the item before saving
                if (SelectedInvoice != null)
                    EditingItem.SupplierInvoiceId = SelectedInvoice.SupplierInvoiceId;

                MainStockDTO savedItem;
                bool isNewItem = EditingItem.MainStockId == 0;

                if (isNewItem)
                    savedItem = await _mainStockService.CreateAsync(EditingItem);
                else
                    savedItem = await _mainStockService.UpdateAsync(EditingItem);

                // Process store product and invoice integration
                try
                {
                    StatusMessage = "Adding product to invoice...";
                    var existingProduct = await _productService.FindProductByBarcodeAsync(savedItem.Barcode);

                    ProductDTO storeProduct;
                    if (existingProduct != null)
                    {
                        storeProduct = new ProductDTO
                        {
                            ProductId = existingProduct.ProductId,
                            Name = savedItem.Name,
                            Barcode = savedItem.Barcode,
                            BoxBarcode = savedItem.BoxBarcode,
                            CategoryId = savedItem.CategoryId,
                            CategoryName = savedItem.CategoryName,
                            SupplierId = savedItem.SupplierId,
                            SupplierName = savedItem.SupplierName,
                            Description = savedItem.Description,
                            PurchasePrice = savedItem.PurchasePrice,
                            WholesalePrice = savedItem.WholesalePrice,
                            SalePrice = savedItem.SalePrice,
                            MainStockId = savedItem.MainStockId,
                            BoxPurchasePrice = savedItem.BoxPurchasePrice,
                            BoxWholesalePrice = savedItem.BoxWholesalePrice,
                            BoxSalePrice = savedItem.BoxSalePrice,
                            ItemsPerBox = savedItem.ItemsPerBox,
                            MinimumBoxStock = savedItem.MinimumBoxStock,
                            MinimumStock = savedItem.MinimumStock,
                            ImagePath = savedItem.ImagePath,
                            Speed = savedItem.Speed,
                            IsActive = savedItem.IsActive,
                            CurrentStock = existingProduct.CurrentStock,
                            UpdatedAt = DateTime.Now
                        };
                        await _productService.UpdateAsync(storeProduct);
                    }
                    else
                    {
                        storeProduct = new ProductDTO
                        {
                            Name = savedItem.Name,
                            Barcode = savedItem.Barcode,
                            BoxBarcode = savedItem.BoxBarcode,
                            CategoryId = savedItem.CategoryId,
                            CategoryName = savedItem.CategoryName,
                            SupplierId = savedItem.SupplierId,
                            SupplierName = savedItem.SupplierName,
                            Description = savedItem.Description,
                            PurchasePrice = savedItem.PurchasePrice,
                            WholesalePrice = savedItem.WholesalePrice,
                            SalePrice = savedItem.SalePrice,
                            MainStockId = savedItem.MainStockId,
                            BoxPurchasePrice = savedItem.BoxPurchasePrice,
                            BoxWholesalePrice = savedItem.BoxWholesalePrice,
                            BoxSalePrice = savedItem.BoxSalePrice,
                            ItemsPerBox = savedItem.ItemsPerBox,
                            MinimumBoxStock = savedItem.MinimumBoxStock,
                            CurrentStock = 0,
                            MinimumStock = savedItem.MinimumStock,
                            ImagePath = savedItem.ImagePath,
                            Speed = savedItem.Speed,
                            IsActive = savedItem.IsActive,
                            CreatedAt = DateTime.Now
                        };
                        storeProduct = await _productService.CreateAsync(storeProduct);
                    }

                    if (SelectedInvoice != null)
                    {
                        var invoiceDetail = new SupplierInvoiceDetailDTO
                        {
                            SupplierInvoiceId = SelectedInvoice.SupplierInvoiceId,
                            ProductId = storeProduct.ProductId,
                            ProductName = savedItem.Name,
                            ProductBarcode = savedItem.Barcode,
                            BoxBarcode = savedItem.BoxBarcode,
                            NumberOfBoxes = savedItem.NumberOfBoxes,
                            ItemsPerBox = savedItem.ItemsPerBox,
                            BoxPurchasePrice = savedItem.BoxPurchasePrice,
                            BoxSalePrice = savedItem.BoxSalePrice,
                            Quantity = savedItem.CurrentStock,
                            PurchasePrice = savedItem.PurchasePrice,
                            TotalPrice = savedItem.PurchasePrice * savedItem.CurrentStock
                        };
                        await _supplierInvoiceService.AddProductToInvoiceAsync(invoiceDetail);
                    }

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        MessageBox.Show($"Product {(isNewItem ? "created" : "updated")} successfully" +
                            (SelectedInvoice != null ? $" and added to invoice '{SelectedInvoice.InvoiceNumber}'" : ""),
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error adding product to invoice: {ex.Message}");
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => {
                        MessageBox.Show($"Product saved but could not be properly linked to the invoice: {ex.Message}",
                            "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }

                StatusMessage = "Changes saved successfully";
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving MainStock item: {ex.Message}");
                StatusMessage = $"Error saving: {ex.Message}";
                MessageBox.Show($"Error saving item: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }
        private void Cancel()
        {
            DialogResult = false;
        }

        private void GenerateBarcode()
        {
            if (EditingItem == null || string.IsNullOrWhiteSpace(EditingItem.Barcode))
            {
                MessageBox.Show("Please enter a barcode first.", "Barcode Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                EditingItem.BarcodeImage = _barcodeService.GenerateBarcode(EditingItem.Barcode);

                // If box barcode is empty, generate it automatically
                if (string.IsNullOrWhiteSpace(EditingItem.BoxBarcode))
                {
                    EditingItem.BoxBarcode = $"BX{EditingItem.Barcode}";
                }

                StatusMessage = "Barcode generated successfully";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating barcode: {ex.Message}");
                StatusMessage = $"Error generating barcode: {ex.Message}";
            }
        }

        private void UploadImage()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Image files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                    Title = "Select an image for the item"
                };

                if (dialog.ShowDialog() == true)
                {
                    string imagePath = _imagePathService.SaveProductImage(dialog.FileName);
                    EditingItem.ImagePath = imagePath;
                    StatusMessage = "Image uploaded successfully";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error uploading image: {ex.Message}");
                StatusMessage = $"Error uploading image: {ex.Message}";
            }
        }

        private void ClearImage()
        {
            if (EditingItem != null && !string.IsNullOrEmpty(EditingItem.ImagePath))
            {
                try
                {
                    _imagePathService.DeleteProductImage(EditingItem.ImagePath);
                    EditingItem.ImagePath = null;
                    StatusMessage = "Image cleared successfully";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error clearing image: {ex.Message}");
                    StatusMessage = $"Error clearing image: {ex.Message}";
                }
            }
        }

        private async Task AddNewCategoryAsync()
        {
            try
            {
                var dialog = new QuickCategoryDialogWindow();
                var result = dialog.ShowDialog();

                if (result == true && dialog.NewCategory != null)
                {
                    var newCategory = await _categoryService.CreateAsync(dialog.NewCategory);
                    Categories.Add(newCategory);
                    SelectedCategory = newCategory;
                    StatusMessage = $"Category '{newCategory.Name}' added successfully";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding category: {ex.Message}");
                StatusMessage = $"Error adding category: {ex.Message}";
            }
        }

        private async Task AddNewSupplierAsync()
        {
            try
            {
                var dialog = new QuickSupplierDialogWindow();
                var result = dialog.ShowDialog();

                if (result == true && dialog.NewSupplier != null)
                {
                    var newSupplier = await _supplierService.CreateAsync(dialog.NewSupplier);
                    Suppliers.Add(newSupplier);
                    SelectedSupplier = newSupplier;
                    StatusMessage = $"Supplier '{newSupplier.Name}' added successfully";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding supplier: {ex.Message}");
                StatusMessage = $"Error adding supplier: {ex.Message}";
            }
        }

        private void ClearInvoice()
        {
            SelectedInvoice = null;
            if (EditingItem != null)
            {
                EditingItem.SupplierInvoiceId = null;
            }
            StatusMessage = "Invoice selection cleared";
        }

        private async Task LookupProductAsync(MainStockDTO item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Barcode))
                return;

            try
            {
                var existingProduct = await _mainStockService.GetByBarcodeAsync(item.Barcode);

                if (existingProduct != null)
                {
                    // Update fields from existing product
                    item.Name = existingProduct.Name;
                    item.Description = existingProduct.Description;
                    item.CategoryId = existingProduct.CategoryId;
                    item.CategoryName = existingProduct.CategoryName;
                    item.SupplierId = existingProduct.SupplierId;
                    item.SupplierName = existingProduct.SupplierName;
                    item.PurchasePrice = existingProduct.PurchasePrice;
                    item.SalePrice = existingProduct.SalePrice;
                    item.BoxBarcode = existingProduct.BoxBarcode;
                    item.BoxPurchasePrice = existingProduct.BoxPurchasePrice;
                    item.BoxSalePrice = existingProduct.BoxSalePrice;
                    item.ItemsPerBox = existingProduct.ItemsPerBox;
                    item.MinimumStock = existingProduct.MinimumStock;
                    item.MinimumBoxStock = existingProduct.MinimumBoxStock;

                    // Update selected values
                    SelectedCategory = Categories?.FirstOrDefault(c => c.CategoryId == item.CategoryId);
                    SelectedSupplier = Suppliers?.FirstOrDefault(s => s.SupplierId == item.SupplierId);

                    StatusMessage = $"Found existing product: {item.Name}";
                }
                else
                {
                    StatusMessage = "New product - please fill in the details";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error looking up product: {ex.Message}");
                StatusMessage = $"Error looking up product: {ex.Message}";
            }
        }

        private async Task LookupBoxBarcodeAsync(MainStockDTO item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.BoxBarcode))
                return;

            try
            {
                var existingProduct = await _mainStockService.GetByBoxBarcodeAsync(item.BoxBarcode);

                if (existingProduct != null)
                {
                    // Update fields from existing product
                    item.Name = existingProduct.Name;
                    item.Barcode = existingProduct.Barcode;
                    item.Description = existingProduct.Description;
                    item.CategoryId = existingProduct.CategoryId;
                    item.CategoryName = existingProduct.CategoryName;
                    item.SupplierId = existingProduct.SupplierId;
                    item.SupplierName = existingProduct.SupplierName;
                    item.PurchasePrice = existingProduct.PurchasePrice;
                    item.SalePrice = existingProduct.SalePrice;
                    item.BoxPurchasePrice = existingProduct.BoxPurchasePrice;
                    item.BoxSalePrice = existingProduct.BoxSalePrice;
                    item.ItemsPerBox = existingProduct.ItemsPerBox;
                    item.MinimumStock = existingProduct.MinimumStock;
                    item.MinimumBoxStock = existingProduct.MinimumBoxStock;

                    // Update selected values
                    SelectedCategory = Categories?.FirstOrDefault(c => c.CategoryId == item.CategoryId);
                    SelectedSupplier = Suppliers?.FirstOrDefault(s => s.SupplierId == item.SupplierId);

                    StatusMessage = $"Found existing product by box barcode: {item.Name}";
                }
                else
                {
                    StatusMessage = "No product found with this box barcode";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error looking up box barcode: {ex.Message}");
                StatusMessage = $"Error looking up box barcode: {ex.Message}";
            }
        }

        private bool ValidateItem()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(EditingItem.Name))
                errors.Add("• Item name is required");

            if (EditingItem.CategoryId <= 0)
                errors.Add("• Please select a category");

            if (EditingItem.SalePrice <= 0)
                errors.Add("• Sale price must be greater than zero");

            if (EditingItem.PurchasePrice < 0)
                errors.Add("• Purchase price cannot be negative");

            if (EditingItem.MinimumStock < 0)
                errors.Add("• Minimum stock cannot be negative");

            if (EditingItem.ItemsPerBox <= 0)
                errors.Add("• Items per box must be greater than zero");

            // Add validation for invoice selection
            if (SelectedInvoice == null)
                errors.Add("• Please select a supplier invoice");

            if (errors.Any())
            {
                MessageBox.Show($"Please fix the following errors:\n\n{string.Join("\n", errors)}",
                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }
    }
}