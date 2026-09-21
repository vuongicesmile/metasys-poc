using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.Contracts;
using DataverseSyncWorker.Models;
using Microsoft.Extensions.Logging;

namespace DataverseSyncWorker.Services;

/// <summary>
/// Điều phối một batch SQL → Dataverse theo thứ tự catalog, current point, history rồi Ack.
/// Thứ tự này bảo đảm lookup cha tồn tại và SQL chỉ đánh dấu thành công sau khi ghi đủ dữ liệu.
/// </summary>
public sealed class SyncEngine(ISyncBatchUnitOfWorkFactory sessions, ISqlCatalogReader catalogReader, ReadingMapper mapper, IDataverseWriter writer,
    SyncOptions options, ILogger<SyncEngine> logger) : ISyncEngine
{
    // Semaphore chống hai batch chạy đồng thời trong cùng process.
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Chạy tối đa một batch; cutoffId giới hạn các dòng thuộc một sync request.</summary>
    public async Task<BatchResult> Run(CancellationToken ct, long? cutoffId = null)
    {
        // Busy không phải lỗi; caller có thể requeue command hoặc đợi vòng sau.
        if (!await _gate.WaitAsync(0, ct)) return new(0, 0, 0, true);
        try
        {
            // App lock có phạm vi SQL session nên connection phải sống suốt batch.
            await using var session = await sessions.TryCreate(ct);
            if (session is null) return new(0, 0, 0, true);
            return await RunLocked(session, ct, cutoffId);
        }
        finally { _gate.Release(); }
    }

    private async Task<BatchResult> RunLocked(ISyncBatchUnitOfWork session, CancellationToken ct, long? cutoffId)
    {
        // Mỗi command đều đồng bộ catalog trước. Building là bản ghi cha nên phải
        // tồn tại trước equipment; equipment phải tồn tại trước lookup của point.
        // Vì vậy catalog vẫn được đọc/ghi kể cả khi không còn reading pending.
        var catalog = await catalogReader.Read(ct);
        await writer.WriteBuildings(catalog.Buildings.Select(mapper.Building).ToArray(), ct);
        await writer.WriteEquipment(catalog.Equipment.Select(mapper.Equipment).ToArray(), ct);
        logger.LogInformation("Catalog synchronized: {Buildings} buildings, {Equipment} equipment",
            catalog.Buildings.Count, catalog.Equipment.Count);
        var batch = await session.ReadBatch( ct, cutoffId);
        // Catalog vẫn được đồng bộ dù không có reading pending để đảm bảo lookup cha tồn tại.
        if (batch.Count == 0) return new(0, 0, 0);
        var valid = new List<BmsReading>();
        foreach (var row in batch)
        {
            // Validation lỗi từng row được quarantine; row hợp lệ tiếp tục cùng batch.
            if (mapper.Validate(row) is { } error) await session.Quarantine( row, error, ct);
            else valid.Add(row);
        }
        var points = new List<DataverseRecord>();
        // Gom theo ObjectId vì nhiều history row chỉ tạo ra một current point mới nhất.
        foreach (var group in valid.GroupBy(r => r.ObjectId, StringComparer.Ordinal))
        {
            // Current state được chọn theo thời gian event mới nhất, không theo id
            // lớn nhất. Nhờ vậy replay/backfill một event cũ không làm tụt giá trị hiện tại.
            var latest = await session.Latest( group.Key, ct);
            if (mapper.Validate(latest) is { } error)
                throw new InvalidOperationException($"Latest reading {latest.Id} requires correction: {error}");
            points.Add(mapper.Point(latest));
        }
        await writer.WritePoints(points, ct);
        if (options.HistoryEnabled)
        {
            // History có TTL theo reading time; mapper sẽ bỏ qua event đã hết hạn.
            var now = DateTime.UtcNow;
            var readings = valid.Select(r => mapper.History(r, now)).OfType<DataverseRecord>().ToArray();
            await writer.WriteHistory(readings, ct);
        }
        // Chỉ acknowledge sau khi point và history (nếu bật) cùng ghi thành công.
        // Nếu Dataverse lỗi giữa chừng, lần chạy sau sẽ replay an toàn nhờ identity cố định.
        foreach (var row in valid) await session.Ack( row, ct);
        logger.LogInformation("Batch {FirstId}..{LastId}: delivered {Delivered}, quarantined {Quarantined}",
            batch[0].Id, batch[^1].Id, valid.Count, batch.Count - valid.Count);
        return new(batch.Count, valid.Count, batch.Count - valid.Count);
    }
}
