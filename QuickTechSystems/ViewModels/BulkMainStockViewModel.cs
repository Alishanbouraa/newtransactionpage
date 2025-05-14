// Path: QuickTechSystems.WPF.ViewModels/BulkMainStockViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using QuickTechSystems.WPF.Views;
using Microsoft.Win32;
using System.IO;
using System.ComponentModel;
using QuickTechSystems.Application.Services;
using System.Windows.Controls;

namespace QuickTechSystems.WPF.ViewModels
{
    /// <summary>
    /// ViewModel for managing bulk operations on MainStock items.
    /// This class handles the creation, modification, and saving of multiple inventory items at once.
    /// </summary>
    public partial class BulkMainStockViewModel : ViewModelBase, IDisposable
    {
        #region Services

        private readonly IMainStockService _mainStockService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly IBarcodeService _barcodeService;
        private readonly ISupplierInvoiceService _supplierInvoiceService;
        private readonly IImagePathService _imagePathService;
        private readonly IProductService _productService;
        private readonly IEventAggregator _eventAggregator;
        private Dictionary<int, List<string>> _validationErrors;
        private readonly IBulkOperationQueueService _bulkOperationQueueService;

        #endregion

        #region Properties

        private bool _isSaving;
        private int _totalRows;
        private int _currentRow;
        private string _statusMessage;
        private ObservableCollection<MainStockDTO> _items;
        private ObservableCollection<CategoryDTO> _categories;
        private ObservableCollection<SupplierDTO> _suppliers;
        private ObservableCollection<SupplierInvoiceDTO> _supplierInvoices;

        // Bulk selection properties
        private CategoryDTO _selectedBulkCategory;
        private SupplierDTO _selectedBulkSupplier;
        private SupplierInvoiceDTO _selectedBulkInvoice;
        private bool _generateBarcodesForNewItems = true;
        private int _labelsPerItem = 1;
        private bool? _dialogResult;

        /// <summary>
        /// Gets or sets a backup for the dialog result that can be used
        /// if the window is closed in a non-standard way.
        /// </summary>
        public bool? DialogResultBackup { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a save operation is in progress.
        /// </summary>
        public bool IsSaving
        {
            get => _isSaving;
            set => SetProperty(ref _isSaving, value);
        }

        /// <summary>
        /// Gets or sets the total number of rows being processed.
        /// </summary>
        public int TotalRows
        {
            get => _totalRows;
            set => SetProperty(ref _totalRows, value);
        }

        /// <summary>
        /// Gets or sets the current row being processed during batch operations.
        /// </summary>
        public int CurrentRow
        {
            get => _currentRow;
            set => SetProperty(ref _currentRow, value);
        }

        /// <summary>
        /// Gets or sets the status message to display to the user.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets or sets the collection of MainStock items being managed.
        /// </summary>
        public ObservableCollection<MainStockDTO> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        /// <summary>
        /// Gets or sets the collection of available categories.
        /// </summary>
        public ObservableCollection<CategoryDTO> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        /// <summary>
        /// Gets or sets the collection of available suppliers.
        /// </summary>
        public ObservableCollection<SupplierDTO> Suppliers
        {
            get => _suppliers;
            set => SetProperty(ref _suppliers, value);
        }

        /// <summary>
        /// Gets or sets the collection of available supplier invoices.
        /// </summary>
        public ObservableCollection<SupplierInvoiceDTO> SupplierInvoices
        {
            get => _supplierInvoices;
            set => SetProperty(ref _supplierInvoices, value);
        }

        /// <summary>
        /// Gets or sets the category selected for bulk application.
        /// </summary>
        public CategoryDTO SelectedBulkCategory
        {
            get => _selectedBulkCategory;
            set => SetProperty(ref _selectedBulkCategory, value);
        }

        /// <summary>
        /// Gets or sets the supplier selected for bulk application.
        /// </summary>
        public SupplierDTO SelectedBulkSupplier
        {
            get => _selectedBulkSupplier;
            set => SetProperty(ref _selectedBulkSupplier, value);
        }

        /// <summary>
        /// Gets or sets the supplier invoice selected for bulk application.
        /// </summary>
        public SupplierInvoiceDTO SelectedBulkInvoice
        {
            get => _selectedBulkInvoice;
            set => SetProperty(ref _selectedBulkInvoice, value);
        }

        /// <summary>
        /// Gets or sets a value indicating whether to automatically generate barcodes for new items.
        /// </summary>
        public bool GenerateBarcodesForNewItems
        {
            get => _generateBarcodesForNewItems;
            set => SetProperty(ref _generateBarcodesForNewItems, value);
        }

        /// <summary>
        /// Gets or sets the number of barcode labels to print per item.
        /// </summary>
        public int LabelsPerItem
        {
            get => _labelsPerItem;
            set => SetProperty(ref _labelsPerItem, Math.Max(1, value));
        }

        /// <summary>
        /// Gets or sets the dialog result for the view.
        /// </summary>
        public bool? DialogResult
        {
            get => _dialogResult;
            set => SetProperty(ref _dialogResult, value);
        }

        #endregion

        #region Commands

        // Commands for data operations
        public ICommand LoadDataCommand { get; private set; }
        public ICommand SaveAllCommand { get; private set; }
        public ICommand AddRowCommand { get; private set; }
        public ICommand RemoveRowCommand { get; private set; }
        public ICommand ClearAllCommand { get; private set; }

        // Barcode-related commands
        public ICommand GenerateAllBarcodesCommand { get; private set; }
        public ICommand ValidateBarcodeCommand { get; private set; }
        public ICommand PrintAllBarcodesCommand { get; private set; }

        // Bulk-related commands
        public ICommand ApplyBulkCategoryCommand { get; private set; }
        public ICommand ApplyBulkSupplierCommand { get; private set; }
        public ICommand ApplyBulkInvoiceCommand { get; private set; }
        public ICommand AddNewCategoryCommand { get; private set; }
        public ICommand AddNewSupplierCommand { get; private set; }
        public ICommand AddNewInvoiceCommand { get; private set; }

        // Image-related commands
        public ICommand UploadItemImageCommand { get; private set; }
        public ICommand ClearItemImageCommand { get; private set; }

        // Box barcode system commands
        public ICommand LookupProductCommand { get; private set; }
        public ICommand LookupBoxBarcodeCommand { get; private set; }

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="BulkMainStockViewModel"/> class.
        /// </summary>
        public BulkMainStockViewModel(
     IMainStockService mainStockService,
     ICategoryService categoryService,
     ISupplierService supplierService,
     IBarcodeService barcodeService,
     ISupplierInvoiceService supplierInvoiceService,
     IImagePathService imagePathService,
     IProductService productService,
     IBulkOperationQueueService bulkOperationQueueService,
     IEventAggregator eventAggregator) : base(eventAggregator)
        {
            // Store services
            _mainStockService = mainStockService;
            _categoryService = categoryService;
            _supplierService = supplierService;
            _barcodeService = barcodeService;
            _supplierInvoiceService = supplierInvoiceService;
            _imagePathService = imagePathService;
            _productService = productService;
            _bulkOperationQueueService = bulkOperationQueueService;
            _eventAggregator = eventAggregator;

            // Initialize collections
            _items = new ObservableCollection<MainStockDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _supplierInvoices = new ObservableCollection<SupplierInvoiceDTO>();

            // Initialize other properties
            _validationErrors = new Dictionary<int, List<string>>();

            // Initialize commands
            InitializeCommands();

            // Add initial row for data entry
            AddNewRow();

            // Load reference data
            _ = LoadDataAsync();
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
        /// <summary>
        /// Initializes all commands used by the view model.
        /// </summary>
        private void InitializeCommands()
        {
            // Data operation commands
            LoadDataCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SaveAllCommand = new AsyncRelayCommand(async _ => await SaveAllAsync());
            AddRowCommand = new RelayCommand(_ => AddNewRow());
            RemoveRowCommand = new RelayCommand<MainStockDTO>(RemoveRow);
            ClearAllCommand = new RelayCommand(_ => ClearAll());

            // Barcode commands
            GenerateAllBarcodesCommand = new RelayCommand(_ => GenerateAllBarcodes());
            ValidateBarcodeCommand = new RelayCommand<MainStockDTO>(ValidateItem);
            PrintAllBarcodesCommand = new AsyncRelayCommand(async _ => await PrintAllBarcodesAsync());

            // Bulk operation commands
            ApplyBulkCategoryCommand = new RelayCommand(_ => ApplyBulkCategory());
            ApplyBulkSupplierCommand = new RelayCommand(_ => ApplyBulkSupplier());
            ApplyBulkInvoiceCommand = new RelayCommand(_ => ApplyBulkInvoice());
            AddNewCategoryCommand = new AsyncRelayCommand(async _ => await AddNewCategoryAsync());
            AddNewSupplierCommand = new AsyncRelayCommand(async _ => await AddNewSupplierAsync());
            AddNewInvoiceCommand = new AsyncRelayCommand(async _ => await AddNewInvoiceAsync());


            // Image commands
            UploadItemImageCommand = new RelayCommand<MainStockDTO>(UploadItemImage);
            ClearItemImageCommand = new RelayCommand<MainStockDTO>(ClearItemImage);

            // Lookup commands
            LookupProductCommand = new AsyncRelayCommand<MainStockDTO>(async item => await LookupProductAsync(item));
            LookupBoxBarcodeCommand = new AsyncRelayCommand<MainStockDTO>(async item => await LookupBoxBarcodeAsync(item));
        }

        /// <summary>
        /// Gets the owner window for dialog operations.
        /// </summary>
        /// <returns>The owner window.</returns>
        private Window GetOwnerWindow()
        {
            // Try to get the active window first
            var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (activeWindow != null)
                return activeWindow;

            // Fall back to the main window
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.IsLoaded)
                return mainWindow;

            // Last resort, get any window that's visible
            return System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                   ?? System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault();
        }

        /// <summary>
        /// Disposes resources used by the view model.
        /// </summary>
        public void Dispose()
        {
            // Unsubscribe from all item property changes to prevent memory leaks
            foreach (var item in Items)
            {
                UnsubscribeFromItemPropertyChanges(item);
            }
        }
    }
}