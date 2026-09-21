using DataverseSyncWorker.Models;
using Microsoft.Data.SqlClient;

namespace DataverseSyncWorker.DataAccess.Abstractions;

/// <summary>
/// Boundary cho các thao tác SQL cần giữ connection/session riêng.
///
/// Các thao tác này không đưa vào EF Core vì app lock phải sống cùng SQL session,
/// còn acknowledge/quarantine cần giữ nguyên transaction và câu lệnh hiện hữu.
/// </summary>
public interface ISqlStore
{
    Task<SqlConnection> Open(CancellationToken ct);
    Task<bool> Lock(SqlConnection connection, CancellationToken ct);
    Task Unlock(SqlConnection connection);
    Task<List<BmsReading>> ReadBatch(SqlConnection connection, CancellationToken ct, long? cutoffId = null);
    Task<BmsReading> Latest(SqlConnection connection, string objectId, CancellationToken ct);
    Task Ack(SqlConnection connection, BmsReading row, CancellationToken ct);
    Task Quarantine(SqlConnection connection, BmsReading row, string reason, CancellationToken ct);
}