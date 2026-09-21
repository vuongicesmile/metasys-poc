using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Abstractions;

/// <summary>
/// Hợp đồng đọc trạng thái giao dữ liệu từ SQL.
/// SQL delivery ledger là nguồn sự thật cho command orchestration; không suy ra
/// backlog chỉ từ MAX(id), vì transaction đến muộn có thể có id thấp hơn.
/// </summary>
public interface ISyncLedger
{
    Task<long> MaxId(CancellationToken ct);
    Task<CutoffSummary> Summary(long cutoffId, CancellationToken ct);
}
