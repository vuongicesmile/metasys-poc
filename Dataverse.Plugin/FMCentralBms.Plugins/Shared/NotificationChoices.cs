namespace FMCentralBms.Plugins
{
    /// <summary>fmc_notification choice values used by the outbox plug-in.</summary>
    internal static class NotificationChoices
    {
        // Các giá trị này phải khớp option-set metadata đã provision trong Dataverse.
        public const int EmailChannel = 789120000;
        public const int Pending = 789121000;
        public const int Skipped = 789121003;
        public const int EventSucceeded = 789122000;
        public const int EventCompletedWithIssues = 789122001;
        public const int EventFailed = 789122002;

        public static int EventTypeForSyncStatus(int syncStatus)
        {
            // Map trạng thái sync sang event type mà flow email dùng để chọn template.
            return syncStatus == SyncRequestStatus.Succeeded ? EventSucceeded
                : syncStatus == SyncRequestStatus.CompletedWithIssues ? EventCompletedWithIssues
                : EventFailed;
        }
    }
}
