using System.Runtime.CompilerServices;
using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Business.Contracts;
using BMS.Ingestion.Business.Services;
using BMS.Ingestion.Common.Configuration;
using BMS.Ingestion.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetasysPoc.Tests;

public sealed class CovIngestionWorkerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sql_setting_controls_persistence_while_events_are_always_observed(bool sqlEnabled)
    {
        var trace = new List<string>();
        var status = new IngestionStatusTracker();
        using var worker = new CovIngestionWorker(new(), new(sqlEnabled), status,
            new Source(trace), new UnitOfWorkFactory(trace), NullLogger<CovIngestionWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshot = status.GetSnapshot();
        Assert.Equal(1, snapshot.EventsReceived);
        Assert.Equal(sqlEnabled ? 1 : 0, snapshot.RowsInserted);
        Assert.Equal("sub-1", snapshot.SubscriptionId);
        Assert.Equal(sqlEnabled
            ? new[] { "catalog", "stage-catalog", "commit", "subscribe:P-1", "stream:sub-1", "add:P-1", "commit" }
            : new[] { "catalog", "subscribe:P-1", "stream:sub-1" }, trace);
    }

    [Fact]
    public async Task Catalog_failure_is_visible_and_prevents_subscription()
    {
        var trace = new List<string>();
        var status = new IngestionStatusTracker();
        using var worker = new CovIngestionWorker(new(), new(false), status,
            new Source(trace) { FailCatalog = true }, new UnitOfWorkFactory(trace), NullLogger<CovIngestionWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Failed", status.GetSnapshot().State);
        Assert.Equal(new[] { "catalog" }, trace);
        Assert.Equal(0, status.GetSnapshot().EventsReceived);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public async Task Commit_failure_is_visible_and_disposes_the_unit_of_work(int failCommit, int eventsReceived)
    {
        var trace = new List<string>();
        var status = new IngestionStatusTracker();
        var factory = new UnitOfWorkFactory(trace) { FailCommit = failCommit };
        using var worker = new CovIngestionWorker(new(), new(true), status,
            new Source(trace), factory, NullLogger<CovIngestionWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Failed", status.GetSnapshot().State);
        Assert.Equal(eventsReceived, status.GetSnapshot().EventsReceived);
        Assert.Equal(0, status.GetSnapshot().RowsInserted);
        Assert.Equal(failCommit, factory.Disposed);
    }

    private sealed class Source(List<string> trace) : IMetasysClient
    {
        public bool FailCatalog;
        public Task<MetasysCatalog> ReadCatalogAsync(CancellationToken ct)
        {
            trace.Add("catalog");
            if (FailCatalog) throw new HttpRequestException("Source unavailable");
            return Task.FromResult(new MetasysCatalog([], [], [new("P-1")]));
        }
        public Task<SubscriptionResponse> SubscribeAsync(IEnumerable<string> ids, CancellationToken ct)
        { trace.Add("subscribe:" + string.Join(",", ids)); return Task.FromResult(new SubscriptionResponse { SubscriptionId = "sub-1" }); }
        public async IAsyncEnumerable<CovEvent> ReadEventsAsync(string id, [EnumeratorCancellation] CancellationToken ct)
        {
            trace.Add("stream:" + id);
            await Task.CompletedTask;
            ct.ThrowIfCancellationRequested();
            yield return new() { ObjectId = "P-1", CurrentValue = 12.3456m };
        }
    }
    private sealed class UnitOfWorkFactory(List<string> trace) : IBmsIngestionUnitOfWorkFactory
    {
        public int FailCommit;
        public int Created;
        public int Disposed;
        public ValueTask<IBmsIngestionUnitOfWork> CreateAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IBmsIngestionUnitOfWork>(new UnitOfWork(trace, this, ++Created));
    }

    private sealed class UnitOfWork(List<string> trace, UnitOfWorkFactory factory, int number)
        : IBmsIngestionUnitOfWork, IBmsReadingRepository, IBmsCatalogRepository
    {
        public IBmsCatalogRepository Catalog => this;
        public IBmsReadingRepository Readings => this;

        public void Add(BmsReadingDto value) => trace.Add("add:" + value.ObjectId);

        public Task StageAsync(BmsCatalogDto catalog, CancellationToken ct = default)
        {
            trace.Add("stage-catalog");
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default)
        {
            trace.Add("commit");
            if (number == factory.FailCommit) throw new InvalidOperationException("Commit failed");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            factory.Disposed++;
            return ValueTask.CompletedTask;
        }
    }
}
