using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;

namespace QuickTechSystems.Application.Services
{
    public class BulkOperationQueueService : IBulkOperationQueueService
    {
        private readonly ConcurrentQueue<MainStockDTO> _itemQueue = new();
        private readonly ConcurrentDictionary<string, ItemStatus> _itemStatus = new();
        private readonly IMainStockService _mainStockService;
        private readonly IEventAggregator _eventAggregator;
        private bool _isProcessing;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly int _batchSize = 10;
        private CancellationTokenSource _cancellationTokenSource;

        public BulkOperationQueueService(IMainStockService mainStockService, IEventAggregator eventAggregator)
        {
            _mainStockService = mainStockService;
            _eventAggregator = eventAggregator;
        }

        public void EnqueueItems(List<MainStockDTO> items)
        {
            foreach (var item in items)
            {
                string itemKey = GetItemKey(item);
                _itemStatus[itemKey] = new ItemStatus { State = ProcessingState.Queued };
                _itemQueue.Enqueue(item);
            }

            StartProcessing();
        }

        public Dictionary<string, ProcessingState> GetAllStatus()
        {
            return _itemStatus.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.State);
        }

        public List<MainStockDTO> GetCompletedItems()
        {
            return _itemStatus
                .Where(kvp => kvp.Value.State == ProcessingState.Completed && kvp.Value.ResultItem != null)
                .Select(kvp => kvp.Value.ResultItem)
                .ToList();
        }

        public List<Tuple<MainStockDTO, string>> GetFailedItems()
        {
            return _itemStatus
                .Where(kvp => kvp.Value.State == ProcessingState.Failed)
                .Select(kvp => new Tuple<MainStockDTO, string>(
                    kvp.Value.OriginalItem,
                    kvp.Value.ErrorMessage ?? "Unknown error"))
                .ToList();
        }

        public bool IsProcessing => _isProcessing;

        public int QueuedItemCount => _itemQueue.Count;

        public int TotalItemCount => _itemStatus.Count;

        public int CompletedItemCount => _itemStatus.Count(s => s.Value.State == ProcessingState.Completed);

        public int FailedItemCount => _itemStatus.Count(s => s.Value.State == ProcessingState.Failed);

        public void CancelProcessing()
        {
            _cancellationTokenSource?.Cancel();
        }

        private async void StartProcessing()
        {
            if (_isProcessing) return;

            await _lock.WaitAsync();
            try
            {
                if (_isProcessing) return;
                _isProcessing = true;

                _cancellationTokenSource = new CancellationTokenSource();

                // Start a background task
                Task.Run(ProcessQueueAsync, _cancellationTokenSource.Token);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                // Report initial status
                _eventAggregator.Publish(new BulkProcessingStatusEvent
                {
                    QueuedItems = _itemQueue.Count,
                    CompletedItems = 0,
                    FailedItems = 0,
                    TotalItems = _itemStatus.Count,
                    IsCompleted = false
                });

                // Track the last reported counts to avoid duplicate messages
                int lastReportedCompleted = 0;
                int lastReportedFailed = 0;

                while (!_itemQueue.IsEmpty && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var batch = new List<MainStockDTO>();
                    var batchItemKeys = new List<string>();

                    // Dequeue up to batch size items
                    for (int i = 0; i < _batchSize && _itemQueue.TryDequeue(out var item); i++)
                    {
                        batch.Add(item);
                        string itemKey = GetItemKey(item);
                        batchItemKeys.Add(itemKey);
                        _itemStatus[itemKey].State = ProcessingState.Processing;
                        _itemStatus[itemKey].OriginalItem = item;
                    }

                    if (batch.Count == 0) break;

                    try
                    {
                        // Process the batch
                        var savedItems = await _mainStockService.CreateBatchAsync(batch);

                        // Update status for each item
                        foreach (var item in batch)
                        {
                            string itemKey = GetItemKey(item);
                            var savedItem = savedItems.FirstOrDefault(s =>
                                (s.Barcode == item.Barcode && !string.IsNullOrEmpty(item.Barcode)) ||
                                (s.Name == item.Name && string.IsNullOrEmpty(item.Barcode)));

                            if (savedItem != null)
                            {
                                _itemStatus[itemKey].State = ProcessingState.Completed;
                                _itemStatus[itemKey].ResultItem = savedItem;
                            }
                            else
                            {
                                _itemStatus[itemKey].State = ProcessingState.Failed;
                                _itemStatus[itemKey].ErrorMessage = "Item was not saved";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing batch: {ex.Message}");

                        // Get the innermost exception message for better error reporting
                        string errorMessage = ex.Message;
                        var currentEx = ex;
                        while (currentEx.InnerException != null)
                        {
                            currentEx = currentEx.InnerException;
                            errorMessage = currentEx.Message;
                        }

                        // Mark all items in batch as failed
                        foreach (var itemKey in batchItemKeys)
                        {
                            _itemStatus[itemKey].State = ProcessingState.Failed;
                            _itemStatus[itemKey].ErrorMessage = errorMessage;
                        }
                    }

                    // Get current counts
                    int currentCompleted = CompletedItemCount;
                    int currentFailed = FailedItemCount;

                    // Only publish progress if the counts have actually changed
                    if (currentCompleted > lastReportedCompleted || currentFailed > lastReportedFailed)
                    {
                        // Publish progress event
                        _eventAggregator.Publish(new BulkProcessingStatusEvent
                        {
                            QueuedItems = _itemQueue.Count,
                            CompletedItems = currentCompleted,
                            FailedItems = currentFailed,
                            TotalItems = _itemStatus.Count,
                            IsCompleted = false
                        });

                        // Update the last reported counts
                        lastReportedCompleted = currentCompleted;
                        lastReportedFailed = currentFailed;
                    }

                    // Give the UI thread a chance to breathe
                    await Task.Delay(50);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Bulk processing was cancelled");
                // No need to do anything, we're cleaning up anyway
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in queue processing: {ex.Message}");

                // Mark all remaining queued items as failed
                while (_itemQueue.TryDequeue(out var item))
                {
                    string itemKey = GetItemKey(item);
                    _itemStatus[itemKey].State = ProcessingState.Failed;
                    _itemStatus[itemKey].ErrorMessage = "Processing error: " + ex.Message;
                }
            }
            finally
            {
                _isProcessing = false;

                // We'll always publish the completion event
                _eventAggregator.Publish(new BulkProcessingStatusEvent
                {
                    QueuedItems = _itemQueue.Count,
                    CompletedItems = CompletedItemCount,
                    FailedItems = FailedItemCount,
                    TotalItems = _itemStatus.Count,
                    IsCompleted = true,
                    IsCompletionMessage = true
                });

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private string GetItemKey(MainStockDTO item)
        {
            return !string.IsNullOrEmpty(item.Barcode)
                ? $"barcode:{item.Barcode}"
                : $"name:{item.Name}:{Guid.NewGuid()}";
        }

        public class ItemStatus
        {
            public ProcessingState State { get; set; }
            public string? ErrorMessage { get; set; }
            public MainStockDTO? ResultItem { get; set; }
            public MainStockDTO? OriginalItem { get; set; }
        }
    }

    public enum ProcessingState
    {
        Queued,
        Processing,
        Completed,
        Failed
    }
}