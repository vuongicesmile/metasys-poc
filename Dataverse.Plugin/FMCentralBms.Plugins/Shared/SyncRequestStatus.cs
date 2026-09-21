namespace FMCentralBms.Plugins
{
    /// <summary>fmc_syncrequest.fmc_status choice values.</summary>
    internal static class SyncRequestStatus
    {
        // Các số nguyên phải khớp choice metadata của fmc_syncrequest.fmc_status.
        public const int Queued = 789100000;
        public const int Running = 789100001;
        public const int Succeeded = 789100002;
        public const int CompletedWithIssues = 789100003;
        public const int Failed = 789100004;

        public static bool IsTerminal(int status)
        {
            // Terminal là trạng thái đủ điều kiện tạo notification outbox.
            return status == Succeeded || status == CompletedWithIssues || status == Failed;
        }

        public static bool IsActive(int status)
        {
            // Active request không được tạo thêm cho cùng pipeline.
            return status == Queued || status == Running;
        }

        public static string Label(int status)
        {
            return status == Succeeded ? "succeeded"
                : status == CompletedWithIssues ? "completed with issues"
                : status == Failed ? "failed"
                : status == Running ? "running"
                : "queued";
        }
    }
}
