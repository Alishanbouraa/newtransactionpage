using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Services;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface IBulkOperationQueueService
    {
        void EnqueueItems(List<MainStockDTO> items);
        Dictionary<string, ProcessingState> GetAllStatus();
        List<MainStockDTO> GetCompletedItems();
        List<Tuple<MainStockDTO, string>> GetFailedItems();
        bool IsProcessing { get; }
        int QueuedItemCount { get; }
        int TotalItemCount { get; }
        int CompletedItemCount { get; }
        int FailedItemCount { get; }
        void CancelProcessing();
    }
}