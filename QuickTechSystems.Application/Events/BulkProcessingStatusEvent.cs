namespace QuickTechSystems.Application.Events
{
    public class BulkProcessingStatusEvent
    {
        public int QueuedItems { get; set; }
        public int CompletedItems { get; set; }
        public int FailedItems { get; set; }
        public int TotalItems { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsCompletionMessage { get; set; } // New field
    }
}