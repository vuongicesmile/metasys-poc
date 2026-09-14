using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace SpoIngestion.Core;

public sealed class BlobJobStore
{
    private readonly BlobContainerClient raw;
    private readonly BlobContainerClient control;
    private readonly QueueClient queue;

    public BlobJobStore(string connectionString, SpoIngestionOptions options)
    {
        raw = new BlobContainerClient(connectionString, options.RawContainer);
        control = new BlobContainerClient(connectionString, options.ControlContainer);
        queue = new QueueClient(connectionString, options.QueueName, new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
    }

    public async Task Initialize(CancellationToken ct)
    {
        await raw.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        await control.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
    }

    public async Task<SpoJobManifest> FinalizeCapture(SpoCaptureRequest request, SpoIngestionOptions options, CancellationToken ct)
    {
        await Initialize(ct);
        var source = SpoConfiguration.Resolve(options, request.SourcePath);
        var rawBlob = raw.GetBlobClient(request.RawBlobName);
        var properties = await rawBlob.GetPropertiesAsync(cancellationToken: ct);
        if (request.ContentLength <= 0 || request.ContentLength > options.MaxFileBytes)
            throw new InvalidDataException($"File size must be 1..{options.MaxFileBytes} bytes.");
        if (properties.Value.ContentLength != request.ContentLength)
            throw new InvalidDataException("Raw blob length differs from capture request.");
        await using var content = await rawBlob.OpenReadAsync(cancellationToken: ct);
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(content, ct));
        var jobKey = Hash($"{request.SourcePath}|{request.SourceETag}|{actualHash}|{source.Mapping}");
        var jobId = jobKey.ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var manifest = new SpoJobManifest(jobId, jobKey, "Ready", request.SourcePath, request.SourceETag,
            request.RawBlobName, actualHash, request.ContentLength, source.Key, source.Mapping, now, now);
        var manifestBlob = Manifest(jobId);
        try
        {
            await manifestBlob.UploadAsync(BinaryData.FromObjectAsJson(manifest, SpoConfiguration.Json),
                new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
        {
            manifest = await Read(jobId, ct);
        }
        if (manifest.Status is "Ready" or "Retrying") await Enqueue(jobId, ct);
        return manifest;
    }

    public async Task Enqueue(string jobId, CancellationToken ct) =>
        await queue.SendMessageAsync(BinaryData.FromObjectAsJson(new SpoJobMessage(jobId), SpoConfiguration.Json), cancellationToken: ct);

    public async Task<SpoJobManifest> Read(string jobId, CancellationToken ct)
    {
        var response = await Manifest(jobId).DownloadContentAsync(ct);
        return response.Value.Content.ToObjectFromJson<SpoJobManifest>(SpoConfiguration.Json)
            ?? throw new InvalidDataException("Invalid job manifest.");
    }

    public async Task<Stream> OpenRaw(SpoJobManifest job, CancellationToken ct) =>
        await raw.GetBlobClient(job.RawBlobName).OpenReadAsync(cancellationToken: ct);

    public async Task<JobLease?> TryLease(string jobId, CancellationToken ct)
    {
        var lease = Manifest(jobId).GetBlobLeaseClient();
        try { await lease.AcquireAsync(TimeSpan.FromSeconds(60), cancellationToken: ct); }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412) { return null; }
        return new JobLease(lease, ct);
    }

    public async Task Save(SpoJobManifest manifest, CancellationToken ct)
    {
        manifest = manifest with { UpdatedAt = DateTimeOffset.UtcNow };
        await Manifest(manifest.JobId).UploadAsync(BinaryData.FromObjectAsJson(manifest, SpoConfiguration.Json), overwrite: true, cancellationToken: ct);
    }

    public async Task Save(SpoJobManifest manifest, JobLease lease, CancellationToken ct)
    {
        manifest = manifest with { UpdatedAt = DateTimeOffset.UtcNow };
        await Manifest(manifest.JobId).UploadAsync(BinaryData.FromObjectAsJson(manifest, SpoConfiguration.Json),
            new BlobUploadOptions { Conditions = new BlobRequestConditions { LeaseId = lease.LeaseId } }, ct);
    }

    public async Task SaveReceipts(string jobId, IReadOnlyList<RowReceipt> receipts, CancellationToken ct) =>
        await control.GetBlobClient($"receipts/{jobId}.json").UploadAsync(
            BinaryData.FromObjectAsJson(receipts, SpoConfiguration.Json), overwrite: true, cancellationToken: ct);

    public async Task SaveReceiptSegment(string jobId, string segment, IReadOnlyList<RowReceipt> receipts, CancellationToken ct) =>
        await control.GetBlobClient($"receipts/{jobId}/{segment}.json").UploadAsync(
            BinaryData.FromObjectAsJson(receipts, SpoConfiguration.Json), overwrite: true, cancellationToken: ct);

    public async IAsyncEnumerable<SpoJobManifest> ReadyJobs([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var blob in control.GetBlobsAsync(BlobTraits.None, BlobStates.None, "manifests/", ct))
        {
            if (!blob.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var id = Path.GetFileNameWithoutExtension(blob.Name);
            var manifest = await Read(id, ct);
            if (manifest.Status is "Ready" or "Retrying" or "WaitingDependency") yield return manifest;
        }
    }

    private BlobClient Manifest(string jobId) => control.GetBlobClient($"manifests/{jobId}.json");
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public sealed class JobLease : IAsyncDisposable
    {
        private readonly BlobLeaseClient lease;
        private readonly CancellationTokenSource stop = new();
        private readonly CancellationTokenSource linked;
        private readonly Task renew;
        public string LeaseId => lease.LeaseId;
        internal JobLease(BlobLeaseClient lease, CancellationToken outer)
        {
            this.lease = lease;
            linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, outer);
            renew = Task.Run(async () =>
            {
                try
                {
                    while (!linked.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(35), linked.Token);
                        await lease.RenewAsync(cancellationToken: linked.Token);
                    }
                }
                catch (OperationCanceledException) { }
            }, linked.Token);
        }
        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            try { await renew; } catch (Exception ex) when (ex is OperationCanceledException or RequestFailedException) { }
            try { await lease.ReleaseAsync(); } catch (RequestFailedException) { }
            linked.Dispose();
            stop.Dispose();
        }
    }
}
