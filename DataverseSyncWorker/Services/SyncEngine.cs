using DataverseSyncWorker.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Xrm.Sdk;

namespace DataverseSyncWorker.Services;

public sealed class SyncEngine(SqlStore store, ReadingMapper mapper, IDataverseWriter writer,
    SyncOptions options, ILogger<SyncEngine> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<BatchResult> Run(CancellationToken ct, long? cutoffId = null)
    {
        if (!await _gate.WaitAsync(0, ct)) return new(0, 0, 0, true);
        try
        {
            await using var c = await store.Open(ct);
            if (!await store.Lock(c, ct)) return new(0, 0, 0, true);
            try { return await RunLocked(c, ct, cutoffId); }
            finally { await store.Unlock(c); }
        }
        finally { _gate.Release(); }
    }

    private async Task<BatchResult> RunLocked(SqlConnection c, CancellationToken ct, long? cutoffId)
    {
        // Catalog is part of every synchronization command. Parent rows are always
        // available before equipment and point lookup writes, even with no readings pending.
        var catalog = await store.ReadCatalog(c, ct);
        await writer.WriteBuildings(catalog.Buildings.Select(mapper.Building).ToArray(), ct);
        await writer.WriteEquipment(catalog.Equipment.Select(mapper.Equipment).ToArray(), ct);
        logger.LogInformation("Catalog synchronized: {Buildings} buildings, {Equipment} equipment",
            catalog.Buildings.Count, catalog.Equipment.Count);
        var batch = await store.ReadBatch(c, ct, cutoffId);
        if (batch.Count == 0) return new(0, 0, 0);
        var valid = new List<BmsReading>();
        foreach (var row in batch)
        {
            if (mapper.Validate(row) is { } error) await store.Quarantine(c, row, error, ct);
            else valid.Add(row);
        }
        var points = new List<Entity>();
        foreach (var group in valid.GroupBy(r => r.ObjectId, StringComparer.Ordinal))
        {
            // Read latest event time from the source so replaying an old batch cannot regress current state.
            var latest = await store.Latest(c, group.Key, ct);
            if (mapper.Validate(latest) is { } error)
                throw new InvalidOperationException($"Latest reading {latest.Id} requires correction: {error}");
            points.Add(mapper.Point(latest));
        }
        await writer.WritePoints(points, ct);
        if (options.HistoryEnabled)
        {
            var now = DateTime.UtcNow;
            var readings = valid.Select(r => mapper.History(r, now)).OfType<Entity>().ToArray();
            await writer.WriteHistory(readings, ct);
        }
        // Never acknowledge before BOTH destinations have completed.
        foreach (var row in valid) await store.Ack(c, row, ct);
        logger.LogInformation("Batch {FirstId}..{LastId}: delivered {Delivered}, quarantined {Quarantined}",
            batch[0].Id, batch[^1].Id, valid.Count, batch.Count - valid.Count);
        return new(batch.Count, valid.Count, batch.Count - valid.Count);
    }
}
