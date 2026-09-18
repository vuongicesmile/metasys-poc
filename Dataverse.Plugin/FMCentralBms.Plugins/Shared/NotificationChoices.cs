namespace FMCentralBms.Plugins
{
    /// <summary>fmc_notification choice values used by the outbox plug-in.</summary>
    internal static class NotificationChoices
    {
        public const int EmailChannel = 789120000;
        public const int Pending = 789121000;
        public const int Skipped = 789121003;
        public const int EventSucceeded = 789122000;
        public const int EventCompletedWithIssues = 789122001;
        public const int EventFailed = 789122002;

        public static int EventTypeForSyncStatus(int syncStatus)
        {
            return syncStatus == SyncRequestStatus.Succeeded ? EventSucceeded
                : syncStatus == SyncRequestStatus.CompletedWithIssues ? EventCompletedWithIssues
                : EventFailed;
        }
    }
}
