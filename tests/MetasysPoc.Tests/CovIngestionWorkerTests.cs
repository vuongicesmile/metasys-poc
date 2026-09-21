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
            new Source(trace), new Repository(trace), new Repository(trace), NullLogger<CovIngestionWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        var snapshot = status.GetSnapshot();
        Assert.Equal(1, snapshot.EventsReceived);
        Assert.Equal(sqlEnabled ? 1 : 0, snapshot.RowsInserted);
        Assert.Equal("sub-1", snapshot.SubscriptionId);
        Assert.Equal(sqlEnabled
            ? new[] { "catalog", "persist-catalog", "subscribe:P-1", "stream:sub-1", "insert:P-1" }
            : new[] { "catalog", "subscribe:P-1", "stream:sub-1" }, trace);
    }

    [Fact]
    public async Task Catalog_failure_is_visible_and_prevents_subscription()
    {
        var trace = new List<string>();
        var status = new IngestionStatusTracker();
        using var worker = new CovIngestionWorker(new(), new(false), status,
            new Source(trace) { FailCatalog = true }, new Repository(trace), new Repository(trace), NullLogger<CovIngestionWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Failed", status.GetSnapshot().State);
        Assert.Equal(new[] { "catalog" }, trace);
        Assert.Equal(0, status.GetSnapshot().EventsReceived);
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
    private sealed class Repository(List<string> trace) : IBmsReadingRepository, IBmsCatalogRepository
    {
        public Task InsertAsync(BmsReadingDto value, CancellationToken ct = default)
        { trace.Add("insert:" + value.ObjectId); return Task.CompletedTask; }
        public Task PersistCatalogAsync(BmsCatalogDto catalog, CancellationToken ct = default)
        { trace.Add("persist-catalog"); return Task.CompletedTask; }
    }
}
