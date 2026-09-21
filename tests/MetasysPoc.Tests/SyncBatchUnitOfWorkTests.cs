using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.Contracts;
using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;
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

    private sealed class Catalog : ISqlCatalogReader
    {
        public Task<(List<BmsBuilding> Buildings, List<BmsEquipment> Equipment)> Read(CancellationToken ct) =>
            Task.FromResult<(List<BmsBuilding>, List<BmsEquipment>)>(([], []));
    }
    private sealed class Session(List<string> trace) : ISyncBatchUnitOfWork, ISyncBatchUnitOfWorkFactory
    {
        private readonly BmsReading row = new(1, "P1", "Point", "Temperature", "B1", "E1", DateTime.UtcNow,
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
    private sealed class Writer(List<string> trace, bool failHistory) : IDataverseWriter
    {
        public Task WriteBuildings(IReadOnlyList<DataverseRecord> rows, CancellationToken ct) => Task.CompletedTask;
        public Task WriteEquipment(IReadOnlyList<DataverseRecord> rows, CancellationToken ct) => Task.CompletedTask;
        public Task WritePoints(IReadOnlyList<DataverseRecord> rows, CancellationToken ct) { trace.Add("points"); return Task.CompletedTask; }
        public Task WriteHistory(IReadOnlyList<DataverseRecord> rows, CancellationToken ct)
        { trace.Add("history"); if (failHistory) throw new InvalidOperationException("Remote failure"); return Task.CompletedTask; }
    }
}
