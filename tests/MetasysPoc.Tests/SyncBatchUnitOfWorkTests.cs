using Dataverse.SyncWorker.Business.Abstractions;
using Dataverse.SyncWorker.Business.Contracts;
using Dataverse.SyncWorker.Business.Services;
using Dataverse.SyncWorker.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetasysPoc.Tests;

public sealed class SyncBatchUnitOfWorkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ack_follows_remote_success_and_session_is_always_released(bool failHistory)
    {
        var trace = new List<string>();
        var options = new SyncOptions();
        var session = new Session(trace);
        var engine = new SyncEngine(session, new Catalog(), new ReadingMapper(options),
            new Writer(trace, failHistory), options, NullLogger<SyncEngine>.Instance);
        if (failHistory) await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Run(default, 25));
        else await engine.Run(default, 25);
        Assert.Equal(25, session.Cutoff);
        Assert.Equal(failHistory ? new[] { "points", "history", "dispose" }
            : new[] { "points", "history", "ack", "dispose" }, trace);
        // The local serialization gate must also be released after failures.
        trace.Clear();
        if (failHistory) await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Run(default));
        else await engine.Run(default);
        Assert.Contains("dispose", trace);
    }

    [Theory]
    [InlineData(1, false, new[] { "points", "snapshot", "ack", "dispose" })]
    [InlineData(1, true, new[] { "points", "snapshot", "dispose" })]
    [InlineData(2, false, new[] { "points", "ack", "dispose" })]
    public async Task Snapshot_receipt_follows_standard_write_only_after_cutover(
        long startSqlId, bool failSnapshot, string[] expected)
    {
        var trace = new List<string>();
        var options = new SyncOptions { HistoryEnabled = false, SnapshotSyncEnabled = true, SnapshotStartSqlId = startSqlId };
        var session = new Session(trace, readingId: 2);
        var engine = new SyncEngine(session, new Catalog(), new ReadingMapper(options),
            new Writer(trace, false, failSnapshot), options, NullLogger<SyncEngine>.Instance);

        if (failSnapshot) await Assert.ThrowsAsync<InvalidOperationException>(() => engine.Run(default));
        else await engine.Run(default);

        Assert.Equal(expected, trace);
    }

    private sealed class Catalog : ISqlCatalogReader
    {
        public Task<(List<BmsBuilding> Buildings, List<BmsEquipment> Equipment)> Read(CancellationToken ct) =>
            Task.FromResult<(List<BmsBuilding>, List<BmsEquipment>)>(([], []));
    }
    private sealed class Session(List<string> trace, long readingId = 1) : ISyncBatchUnitOfWork, ISyncBatchUnitOfWorkFactory
    {
        private readonly BmsReading row = new(readingId, "P1", "Point", "Temperature", "B1", "E1", DateTime.UtcNow,
            12.3456m, "C", "Fake Metasys COV", DateTime.UtcNow);
        public long? Cutoff;
        public Task<ISyncBatchUnitOfWork?> TryCreate(CancellationToken ct) => Task.FromResult<ISyncBatchUnitOfWork?>(this);
        public Task<List<BmsReading>> ReadBatch(CancellationToken ct, long? cutoffId = null)
        { Cutoff = cutoffId; return Task.FromResult(new List<BmsReading> { row }); }
        public Task<BmsReading> Latest(string objectId, CancellationToken ct) => Task.FromResult(row);
        public Task Ack(BmsReading row, CancellationToken ct) { trace.Add("ack"); return Task.CompletedTask; }
        public Task Quarantine(BmsReading row, string reason, CancellationToken ct) => throw new Exception(reason);
        public ValueTask DisposeAsync() { trace.Add("dispose"); return ValueTask.CompletedTask; }
    }
    private sealed class Writer(List<string> trace, bool failHistory, bool failSnapshot = false) : IDataverseWriter
    {
        public Task WriteBuildings(IReadOnlyList<DataverseRecord> rows, CancellationToken ct) => Task.CompletedTask;
        public Task WriteEquipment(IReadOnlyList<DataverseRecord> rows, CancellationToken ct) => Task.CompletedTask;
        public Task WritePoints(IReadOnlyList<DataverseRecord> rows, CancellationToken ct) { trace.Add("points"); return Task.CompletedTask; }
        public Task WriteHistory(IReadOnlyList<DataverseRecord> rows, CancellationToken ct)
        { trace.Add("history"); if (failHistory) throw new InvalidOperationException("Remote failure"); return Task.CompletedTask; }
        public Task WriteSnapshot(IReadOnlyList<DataverseRecord> rows, CancellationToken ct)
        {
            if (rows.Count == 0) return Task.CompletedTask;
            trace.Add("snapshot");
            if (failSnapshot) throw new InvalidOperationException("Remote snapshot failure");
            return Task.CompletedTask;
        }
    }
}
