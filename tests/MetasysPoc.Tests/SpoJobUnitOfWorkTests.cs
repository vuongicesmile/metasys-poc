using SPO.Ingestion.Business.Abstractions;
using SPO.Ingestion.DataAccess;
using SPO.Ingestion.Domain;

namespace MetasysPoc.Tests;

public sealed class SpoJobUnitOfWorkTests
{
    [Fact]
    public async Task Busy_job_returns_no_session()
    {
        var store = new Store { Busy = true };
        Assert.Null(await new SpoJobUnitOfWorkFactory(store).TryCreate("job", default));
        Assert.False(store.Lease.Disposed);
    }

    [Fact]
    public async Task Session_binds_manifest_and_receipts_to_its_lease_and_releases_on_failure()
    {
        var store = new Store();
        var manifest = new SpoJobManifest("job", "key", "Processing", "path", "etag", "blob", "sha", 1,
            "source", "v1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await using (var session = await new SpoJobUnitOfWorkFactory(store).TryCreate("job", default))
        {
            Assert.NotNull(session);
            await session.Save(manifest, default);
            Assert.Same(store.Lease, store.SavedLease);
            await session.SaveReceiptSegment("points", [], default);
            Assert.Equal("job:points", store.ReceiptKey);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Save(manifest with { JobId = "other" }, default));
        }
        Assert.True(store.Lease.Disposed);
    }

    private sealed class Lease : JobLease
    {
        public string LeaseId => "lease";
        public bool Disposed;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Store : ISpoJobStore
    {
        public bool Busy;
        public Lease Lease = new();
        public JobLease? SavedLease;
        public string? ReceiptKey;
        public Task<JobLease?> TryLease(string id, CancellationToken ct) => Task.FromResult<JobLease?>(Busy ? null : Lease);
        public Task Save(SpoJobManifest job, JobLease lease, CancellationToken ct) { SavedLease = lease; return Task.CompletedTask; }
        public Task SaveReceiptSegment(string id, string segment, IReadOnlyList<RowReceipt> receipts, CancellationToken ct)
        { ReceiptKey = id + ":" + segment; return Task.CompletedTask; }
        public Task Initialize(CancellationToken ct) => throw new NotSupportedException();
        public Task<SpoJobManifest> Read(string id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Stream> OpenRaw(SpoJobManifest job, CancellationToken ct) => throw new NotSupportedException();
        public Task Save(SpoJobManifest job, CancellationToken ct) => throw new NotSupportedException();
        public Task SaveReceipts(string id, IReadOnlyList<RowReceipt> receipts, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<SpoJobManifest> ReadyJobs(CancellationToken ct) => throw new NotSupportedException();
    }
}
