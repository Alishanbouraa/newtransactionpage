// QuickTechSystems/ViewModels/BulkProcessingStatusViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;
using System.ComponentModel;

namespace QuickTechSystems.WPF.ViewModels
{
    /// <summary>
    /// ViewModel for tracking and displaying bulk processing status
    /// </summary>
    public class BulkProcessingStatusViewModel : ViewModelBase
    {
        #region Private Fields

        private readonly IBulkOperationQueueService _queueService;
        private int _totalItems;
        private int _completedItems;
        private int _failedItems;
        private int _queuedItems;
        private bool _isProcessing;
        private string _statusMessage;
        private string _timeElapsed;
        private int _progressPercentage;
        private int _successRate;
        private ObservableCollection<string> _logMessages;
        private ObservableCollection<ErrorCategoryViewModel> _errorCategories;
        private bool _canRetry;

        #endregion

        #region Events

        /// <summary>
        /// Event raised when the dialog should be closed
        /// </summary>
        public event EventHandler<DialogResultEventArgs> CloseRequested;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets or sets the total number of items being processed
        /// </summary>
        public int TotalItems
        {
            get => _totalItems;
            set => SetProperty(ref _totalItems, value);
        }

        /// <summary>
        /// Gets or sets the number of items that have been successfully processed
        /// </summary>
        public int CompletedItems
        {
            get => _completedItems;
            set => SetProperty(ref _completedItems, value);
        }

        /// <summary>
        /// Gets or sets the number of items that failed processing
        /// </summary>
        public int FailedItems
        {
            get => _failedItems;
            set => SetProperty(ref _failedItems, value);
        }

        /// <summary>
        /// Gets or sets the number of items waiting to be processed
        /// </summary>
        public int QueuedItems
        {
            get => _queuedItems;
            set => SetProperty(ref _queuedItems, value);
        }

        /// <summary>
        /// Gets or sets whether processing is currently in progress
        /// </summary>
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    OnPropertyChanged(nameof(CanRetry));
                }
            }
        }

        /// <summary>
        /// Gets or sets the current status message
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets or sets the elapsed time as a formatted string
        /// </summary>
        public string TimeElapsed
        {
            get => _timeElapsed;
            set => SetProperty(ref _timeElapsed, value);
        }

        /// <summary>
        /// Gets or sets the progress percentage (0-100)
        /// </summary>
        public int ProgressPercentage
        {
            get => _progressPercentage;
            set => SetProperty(ref _progressPercentage, value);
        }

        /// <summary>
        /// Gets or sets the success rate percentage (0-100)
        /// </summary>
        public int SuccessRate
        {
            get => _successRate;
            set => SetProperty(ref _successRate, value);
        }

        /// <summary>
        /// Gets or sets the collection of log messages
        /// </summary>
        public ObservableCollection<string> LogMessages
        {
            get => _logMessages;
            set => SetProperty(ref _logMessages, value);
        }

        /// <summary>
        /// Gets or sets the collection of error categories
        /// </summary>
        public ObservableCollection<ErrorCategoryViewModel> ErrorCategories
        {
            get => _errorCategories;
            set => SetProperty(ref _errorCategories, value);
        }

        /// <summary>
        /// Gets whether items can be retried
        /// </summary>
        public bool CanRetry
        {
            get => _canRetry && !IsProcessing;
            set => SetProperty(ref _canRetry, value);
        }

        #endregion

        #region Commands

        /// <summary>
        /// Command to cancel processing
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Command to close the dialog
        /// </summary>
        public ICommand CloseCommand { get; }

        /// <summary>
        /// Command to retry failed items
        /// </summary>
        public ICommand RetryCommand { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="BulkProcessingStatusViewModel"/> class
        /// </summary>
        /// <param name="queueService">The bulk operation queue service</param>
        /// <param name="eventAggregator">The event aggregator</param>
        public BulkProcessingStatusViewModel(IBulkOperationQueueService queueService, IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _queueService = queueService;

            // Initialize commands
            CancelCommand = new RelayCommand(_ => CancelProcessing());
            CloseCommand = new RelayCommand(_ => RequestClose());
            RetryCommand = new AsyncRelayCommand(async _ => await RetryFailedItemsAsync());

            // Initialize collections
            LogMessages = new ObservableCollection<string>();
            ErrorCategories = new ObservableCollection<ErrorCategoryViewModel>();

            // Subscribe to progress events
            eventAggregator.Subscribe<BulkProcessingStatusEvent>(HandleStatusUpdate);

            // Initialize with current status
            IsProcessing = queueService.IsProcessing;
            TotalItems = queueService.TotalItemCount;
            CompletedItems = queueService.CompletedItemCount;
            FailedItems = queueService.FailedItemCount;
            QueuedItems = queueService.QueuedItemCount;
            CanRetry = FailedItems > 0;

            UpdateStatusMessage();

            AddLogMessage($"Started processing {TotalItems} items...");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Updates the status message based on current state
        /// </summary>
        private void UpdateStatusMessage()
        {
            if (IsProcessing)
            {
                StatusMessage = $"Processing items... {CompletedItems + FailedItems} of {TotalItems} completed.";
            }
            else if (TotalItems == 0)
            {
                StatusMessage = "No items to process.";
            }
            else if (FailedItems > 0)
            {
                StatusMessage = $"Processing completed with {FailedItems} failures. You can retry failed items.";
                CanRetry = true;
            }
            else
            {
                StatusMessage = "Processing completed successfully!";
                CanRetry = false;
            }
        }

        /// <summary>
        /// Handles a status update event
        /// </summary>
        /// <param name="e">The status update event</param>
        private void HandleStatusUpdate(BulkProcessingStatusEvent e)
        {
            try
            {
                // Update on UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    TotalItems = e.TotalItems;
                    CompletedItems = e.CompletedItems;
                    FailedItems = e.FailedItems;
                    QueuedItems = e.QueuedItems;
                    IsProcessing = !e.IsCompleted;
                    ProgressPercentage = e.ProgressPercentage;
                    SuccessRate = e.SuccessPercentage;

                    // Update elapsed time
                    TimeElapsed = FormatTimeSpan(e.ElapsedTime);

                    // Update error categories
                    UpdateErrorCategories(e.ErrorCategories);

                    UpdateStatusMessage();

                    // Only log processing progress at certain points for smaller batches
                    if ((CompletedItems > 0 && CompletedItems % 10 == 0) ||
                        (CompletedItems == TotalItems && !e.IsCompletionMessage))
                    {
                        AddLogMessage($"Processed {CompletedItems} of {TotalItems} items ({ProgressPercentage}% complete).");
                    }

                    if (FailedItems > 0 && (FailedItems % 5 == 0 || FailedItems == 1))
                    {
                        AddLogMessage($"Warning: {FailedItems} items have failed.");
                    }

                    // Use a different format for the completion message
                    if (e.IsCompleted && e.IsCompletionMessage)
                    {
                        string completionMessage = FailedItems > 0
                            ? $"Processing completed with {CompletedItems} successes ({SuccessRate}%) and {FailedItems} failures."
                            : $"Processing completed successfully! All {CompletedItems} items saved.";

                        AddLogMessage(completionMessage);
                        AddLogMessage($"Total processing time: {TimeElapsed}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling status update: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the error categories collection
        /// </summary>
        /// <param name="categories">The error categories with counts</param>
        private void UpdateErrorCategories(Dictionary<string, int> categories)
        {
            if (categories == null || categories.Count == 0)
                return;

            // Check for new categories or update existing ones
            foreach (var category in categories)
            {
                var existingCategory = ErrorCategories.FirstOrDefault(c => c.Category == category.Key);

                if (existingCategory != null)
                {
                    existingCategory.Count = category.Value;
                }
                else
                {
                    ErrorCategories.Add(new ErrorCategoryViewModel
                    {
                        Category = category.Key,
                        Count = category.Value,
                        Description = GetErrorCategoryDescription(category.Key)
                    });
                }
            }
        }

        /// <summary>
        /// Gets a user-friendly description for an error category
        /// </summary>
        /// <param name="category">The error category</param>
        /// <returns>A user-friendly description of the error category</returns>
        private string GetErrorCategoryDescription(string category)
        {
            switch (category)
            {
                case "DatabaseError":
                    return "Database related errors";
                case "DuplicateEntry":
                    return "Duplicate or conflicting data entries";
                case "ValidationError":
                    return "Data validation failures";
                case "Timeout":
                    return "Operation timeout issues";
                case "ReferenceError":
                    return "Missing references or null data";
                case "DataSaveFailed":
                    return "Failed to save item to database";
                default:
                    return $"{category} errors";
            }
        }

        /// <summary>
        /// Formats a TimeSpan as a user-friendly string
        /// </summary>
        /// <param name="timeSpan">The TimeSpan to format</param>
        /// <returns>A formatted string representation of the TimeSpan</returns>
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
            {
                return $"{timeSpan.Hours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
            }

            if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.Minutes}m {timeSpan.Seconds}s";
            }

            if (timeSpan.TotalSeconds >= 10)
            {
                return $"{timeSpan.Seconds}.{timeSpan.Milliseconds / 100}s";
            }

            return $"{timeSpan.TotalSeconds:F2}s";
        }

        /// <summary>
        /// Adds a log message to the LogMessages collection
        /// </summary>
        /// <param name="message">The message to add</param>
        private void AddLogMessage(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogMessages.Add($"[{timestamp}] {message}");

                // Keep a reasonable number of log messages
                const int maxLogMessages = 100;
                while (LogMessages.Count > maxLogMessages)
                {
                    LogMessages.RemoveAt(0);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding log message: {ex.Message}");
            }
        }

        /// <summary>
        /// Cancels the current processing operation
        /// </summary>
        private void CancelProcessing()
        {
            if (!IsProcessing) return;

            try
            {
                _queueService.CancelProcessing();
                AddLogMessage("Cancellation requested. Waiting for current operations to complete...");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cancelling processing: {ex.Message}");
                AddLogMessage($"Error cancelling processing: {ex.Message}");
            }
        }

        /// <summary>
        /// Retries failed items
        /// </summary>
        private async Task RetryFailedItemsAsync()
        {
            if (!CanRetry || IsProcessing) return;

            try
            {
                AddLogMessage($"Retrying {FailedItems} failed items...");
                await _queueService.RetryFailedItemsAsync();

                // Reset CanRetry until we get status updates
                CanRetry = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error retrying failed items: {ex.Message}");
                AddLogMessage($"Error retrying items: {ex.Message}");
            }
        }

        /// <summary>
        /// Requests that the dialog be closed
        /// </summary>
        private void RequestClose()
        {
            bool allowClose = !IsProcessing ||
                              System.Windows.MessageBox.Show(
                                  "Processing is still in progress. Are you sure you want to close this window?",
                                  "Confirm Close",
                                  System.Windows.MessageBoxButton.YesNo,
                                  System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

            if (allowClose)
            {
                CloseRequested?.Invoke(this, new DialogResultEventArgs(true));
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Disposes resources used by the ViewModel
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();

            // Unsubscribe from events
            _eventAggregator.Unsubscribe<BulkProcessingStatusEvent>(HandleStatusUpdate);
        }

        #endregion
    }

    /// <summary>
    /// ViewModel for an error category
    /// </summary>
    public class ErrorCategoryViewModel : INotifyPropertyChanged
    {
        private string _category;
        private int _count;
        private string _description;

        /// <summary>
        /// Gets or sets the error category
        /// </summary>
        public string Category
        {
            get => _category;
            set
            {
                _category = value;
                OnPropertyChanged(nameof(Category));
            }
        }

        /// <summary>
        /// Gets or sets the number of errors in this category
        /// </summary>
        public int Count
        {
            get => _count;
            set
            {
                _count = value;
                OnPropertyChanged(nameof(Count));
            }
        }

        /// <summary>
        /// Gets or sets the description of the error category
        /// </summary>
        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged(nameof(Description));
            }
        }

        /// <summary>
        /// Event that is raised when a property value changes
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the PropertyChanged event
        /// </summary>
        /// <param name="propertyName">The name of the property that changed</param>
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Event arguments for dialog result events
    /// </summary>
    public class DialogResultEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the dialog result
        /// </summary>
        public bool DialogResult { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DialogResultEventArgs"/> class
        /// </summary>
        /// <param name="dialogResult">The dialog result</param>
        public DialogResultEventArgs(bool dialogResult)
        {
            DialogResult = dialogResult;
        }
    }
}