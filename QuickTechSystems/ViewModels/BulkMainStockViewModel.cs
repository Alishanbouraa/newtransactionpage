using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using QuickTechSystems.Application.Services;

namespace QuickTechSystems.WPF.ViewModels
{
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
        private CategoryDTO _selectedBulkCategory;
        private SupplierDTO _selectedBulkSupplier;
        private SupplierInvoiceDTO _selectedBulkInvoice;
        private bool _generateBarcodesForNewItems = true;
        private int _labelsPerItem = 1;
        private bool? _dialogResult;
        private bool _isCategorySelected;
        private bool _isSupplierSelected;
        private bool _isInvoiceSelected;

        public bool? DialogResultBackup { get; set; }

        public bool IsSaving
        {
            get => _isSaving;
            set => SetProperty(ref _isSaving, value);
        }

        public int TotalRows
        {
            get => _totalRows;
            set => SetProperty(ref _totalRows, value);
        }

        public int CurrentRow
        {
            get => _currentRow;
            set => SetProperty(ref _currentRow, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<MainStockDTO> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
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

        public ObservableCollection<SupplierInvoiceDTO> SupplierInvoices
        {
            get => _supplierInvoices;
            set => SetProperty(ref _supplierInvoices, value);
        }

        public CategoryDTO SelectedBulkCategory
        {
            get => _selectedBulkCategory;
            set
            {
                if (SetProperty(ref _selectedBulkCategory, value))
                {
                    IsCategorySelected = value != null;
                    OnPropertyChanged(nameof(AreRequiredFieldsFilled));
                }
            }
        }

        public SupplierDTO SelectedBulkSupplier
        {
            get => _selectedBulkSupplier;
            set
            {
                if (SetProperty(ref _selectedBulkSupplier, value))
                {
                    IsSupplierSelected = value != null;
                    OnPropertyChanged(nameof(AreRequiredFieldsFilled));
                }
            }
        }

        public SupplierInvoiceDTO SelectedBulkInvoice
        {
            get => _selectedBulkInvoice;
            set
            {
                if (SetProperty(ref _selectedBulkInvoice, value))
                {
                    IsInvoiceSelected = value != null;
                    OnPropertyChanged(nameof(AreRequiredFieldsFilled));
                }
            }
        }

        public bool GenerateBarcodesForNewItems
        {
            get => _generateBarcodesForNewItems;
            set => SetProperty(ref _generateBarcodesForNewItems, value);
        }

        public int LabelsPerItem
        {
            get => _labelsPerItem;
            set => SetProperty(ref _labelsPerItem, Math.Max(1, value));
        }

        public bool? DialogResult
        {
            get => _dialogResult;
            set => SetProperty(ref _dialogResult, value);
        }

        public bool IsCategorySelected
        {
            get => _isCategorySelected;
            set => SetProperty(ref _isCategorySelected, value);
        }

        public bool IsSupplierSelected
        {
            get => _isSupplierSelected;
            set => SetProperty(ref _isSupplierSelected, value);
        }

        public bool IsInvoiceSelected
        {
            get => _isInvoiceSelected;
            set => SetProperty(ref _isInvoiceSelected, value);
        }

        public bool AreRequiredFieldsFilled
        {
            get => IsCategorySelected && IsSupplierSelected && IsInvoiceSelected;
        }

        #endregion

        #region Commands

        public ICommand LoadDataCommand { get; private set; }
        public ICommand SaveAllCommand { get; private set; }
        public ICommand AddRowCommand { get; private set; }
        public ICommand RemoveRowCommand { get; private set; }
        public ICommand ClearAllCommand { get; private set; }
        public ICommand GenerateAllBarcodesCommand { get; private set; }
        public ICommand ValidateBarcodeCommand { get; private set; }
        public ICommand PrintAllBarcodesCommand { get; private set; }
        public ICommand ApplyBulkCategoryCommand { get; private set; }
        public ICommand ApplyBulkSupplierCommand { get; private set; }
        public ICommand ApplyBulkInvoiceCommand { get; private set; }
        public ICommand AddNewCategoryCommand { get; private set; }
        public ICommand AddNewSupplierCommand { get; private set; }
        public ICommand AddNewInvoiceCommand { get; private set; }
        public ICommand UploadItemImageCommand { get; private set; }
        public ICommand ClearItemImageCommand { get; private set; }
        public ICommand LookupProductCommand { get; private set; }
        public ICommand LookupBoxBarcodeCommand { get; private set; }

        #endregion

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
            _mainStockService = mainStockService;
            _categoryService = categoryService;
            _supplierService = supplierService;
            _barcodeService = barcodeService;
            _supplierInvoiceService = supplierInvoiceService;
            _imagePathService = imagePathService;
            _productService = productService;
            _bulkOperationQueueService = bulkOperationQueueService;
            _eventAggregator = eventAggregator;

            _items = new ObservableCollection<MainStockDTO>();
            _categories = new ObservableCollection<CategoryDTO>();
            _suppliers = new ObservableCollection<SupplierDTO>();
            _supplierInvoices = new ObservableCollection<SupplierInvoiceDTO>();

            _validationErrors = new Dictionary<int, List<string>>();
            IsCategorySelected = false;
            IsSupplierSelected = false;
            IsInvoiceSelected = false;

            InitializeCommands();

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

        private void InitializeCommands()
        {
            LoadDataCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SaveAllCommand = new AsyncRelayCommand(async _ => await SaveAllAsync());
            AddRowCommand = new RelayCommand(_ => AddNewRow());
            RemoveRowCommand = new RelayCommand<MainStockDTO>(RemoveRow);
            ClearAllCommand = new RelayCommand(_ => ClearAll());

            GenerateAllBarcodesCommand = new RelayCommand(_ => GenerateAllBarcodes());
            ValidateBarcodeCommand = new RelayCommand<MainStockDTO>(ValidateItem);
            PrintAllBarcodesCommand = new AsyncRelayCommand(async _ => await PrintAllBarcodesAsync());

            ApplyBulkCategoryCommand = new RelayCommand(_ => ApplyBulkCategory());
            ApplyBulkSupplierCommand = new RelayCommand(_ => ApplyBulkSupplier());
            ApplyBulkInvoiceCommand = new RelayCommand(_ => ApplyBulkInvoice());
            AddNewCategoryCommand = new AsyncRelayCommand(async _ => await AddNewCategoryAsync());
            AddNewSupplierCommand = new AsyncRelayCommand(async _ => await AddNewSupplierAsync());
            AddNewInvoiceCommand = new AsyncRelayCommand(async _ => await AddNewInvoiceAsync());

            UploadItemImageCommand = new RelayCommand<MainStockDTO>(UploadItemImage);
            ClearItemImageCommand = new RelayCommand<MainStockDTO>(ClearItemImage);

            LookupProductCommand = new AsyncRelayCommand<MainStockDTO>(async item => await LookupProductAsync(item));
            LookupBoxBarcodeCommand = new AsyncRelayCommand<MainStockDTO>(async item => await LookupBoxBarcodeAsync(item));
        }

        private Window GetOwnerWindow()
        {
            var activeWindow = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (activeWindow != null)
                return activeWindow;

            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null && mainWindow.IsLoaded)
                return mainWindow;

            return System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible)
                   ?? System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault();
        }

        public void Dispose()
        {
            foreach (var item in Items)
            {
                UnsubscribeFromItemPropertyChanges(item);
            }
        }
    }
}