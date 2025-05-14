using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Diagnostics;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.WPF.Commands;

namespace QuickTechSystems.WPF.ViewModels
{
    public class BulkProcessingStatusViewModel : ViewModelBase
    {
        private readonly IBulkOperationQueueService _queueService;
        private int _totalItems;
        private int _completedItems;
        private int _failedItems;
        private int _queuedItems;
        private bool _isProcessing;
        private string _statusMessage;
        private ObservableCollection<string> _logMessages;

        public event EventHandler<DialogResultEventArgs> CloseRequested;

        public int TotalItems
        {
            get => _totalItems;
            set => SetProperty(ref _totalItems, value);
        }

        public int CompletedItems
        {
            get => _completedItems;
            set => SetProperty(ref _completedItems, value);
        }

        public int FailedItems
        {
            get => _failedItems;
            set => SetProperty(ref _failedItems, value);
        }

        public int QueuedItems
        {
            get => _queuedItems;
            set => SetProperty(ref _queuedItems, value);
        }

        public bool IsProcessing
        {
            get => _isProcessing;
            set => SetProperty(ref _isProcessing, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<string> LogMessages
        {
            get => _logMessages;
            set => SetProperty(ref _logMessages, value);
        }

        public ICommand CancelCommand { get; }
        public ICommand CloseCommand { get; }

        public BulkProcessingStatusViewModel(IBulkOperationQueueService queueService, IEventAggregator eventAggregator)
            : base(eventAggregator)
        {
            _queueService = queueService;

            CancelCommand = new RelayCommand(_ => CancelProcessing());
            CloseCommand = new RelayCommand(_ => RequestClose());

            LogMessages = new ObservableCollection<string>();

            // Subscribe to progress events
            eventAggregator.Subscribe<BulkProcessingStatusEvent>(HandleStatusUpdate);

            // Initialize with current status
            IsProcessing = queueService.IsProcessing;
            TotalItems = queueService.TotalItemCount;
            CompletedItems = queueService.CompletedItemCount;
            FailedItems = queueService.FailedItemCount;
            QueuedItems = queueService.QueuedItemCount;

            UpdateStatusMessage();

            AddLogMessage($"Started processing {TotalItems} items...");
        }

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
                StatusMessage = $"Processing completed with {FailedItems} failures.";
            }
            else
            {
                StatusMessage = "Processing completed successfully!";
            }
        }

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

                    UpdateStatusMessage();

                    // Only log processing progress once for smaller batches
                    if ((CompletedItems > 0 && CompletedItems % 10 == 0) ||
                        (CompletedItems == TotalItems && !e.IsCompletionMessage))
                    {
                        AddLogMessage($"Processed {CompletedItems} of {TotalItems} items.");
                    }

                    if (FailedItems > 0 && FailedItems % 5 == 0)
                    {
                        AddLogMessage($"Warning: {FailedItems} items have failed.");
                    }

                    // Use a different format for the completion message
                    if (e.IsCompleted && e.IsCompletionMessage)
                    {
                        string completionMessage = FailedItems > 0
                            ? $"Processing completed with {CompletedItems} successes and {FailedItems} failures."
                            : $"Processing completed successfully! All {CompletedItems} items saved.";

                        AddLogMessage(completionMessage);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling status update: {ex.Message}");
            }
        }
        private void AddLogMessage(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                LogMessages.Add($"[{timestamp}] {message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding log message: {ex.Message}");
            }
        }

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

        public override void Dispose()
        {
            base.Dispose();

            // Unsubscribe from events
            _eventAggregator.Unsubscribe<BulkProcessingStatusEvent>(HandleStatusUpdate);
        }
    }

    public class DialogResultEventArgs : EventArgs
    {
        public bool DialogResult { get; }

        public DialogResultEventArgs(bool dialogResult)
        {
            DialogResult = dialogResult;
        }
    }
}