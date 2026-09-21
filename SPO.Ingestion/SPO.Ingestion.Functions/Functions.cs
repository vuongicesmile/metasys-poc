using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using SPO.Ingestion.Business;
using SPO.Ingestion.Common;
using SPO.Ingestion.DataAccess;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Functions;

public sealed class Functions(BlobJobStore store, SpoJobProcessor processor, SpoIngestionOptions options, ILogger<Functions> logger)
{
    [Function("FinalizeSpoCapture")]
    public async Task<HttpResponseData> Finalize(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "spo/captures/finalize")] HttpRequestData request,
        CancellationToken ct)
    {
        try
        {
            var capture = await JsonSerializer.DeserializeAsync<SpoCaptureRequest>(request.Body, SpoConfiguration.Json, ct)
                ?? throw new InvalidDataException("Request body is required.");
            var manifest = await store.FinalizeCapture(capture, options, ct);
            var accepted = request.CreateResponse(HttpStatusCode.Accepted);
            await accepted.WriteAsJsonAsync(new { manifest.JobId, manifest.JobKey, manifest.Status }, ct);
            return accepted;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or JsonException)
        {
            logger.LogWarning(ex, "SPO capture was rejected");
            var rejected = request.CreateResponse(HttpStatusCode.BadRequest);
            await rejected.WriteAsJsonAsync(new { error = ex.Message }, ct);
            return rejected;
        }
    }

    [Function("ProcessSpoJob")]
    public async Task Process(
        [QueueTrigger("%SpoQueueName%", Connection = "SpoStorage")] SpoJobMessage message,
        CancellationToken ct)
    {
        logger.LogInformation("Processing SPO job {JobId}", message.JobId);
        var result = await processor.Process(message.JobId, ct);
        logger.LogInformation("SPO job {JobId} ended as {Status}; delivered={Delivered}; skipped={Skipped}",
            result.JobId, result.Status, result.Delivered, result.Skipped);
    }

    [Function("ReconcileSpoJobs")]
    public async Task Reconcile([TimerTrigger("0 */5 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        await foreach (var job in store.ReadyJobs(ct))
        {
            await store.Enqueue(job.JobId, ct);
            logger.LogInformation("Re-enqueued recoverable SPO job {JobId}", job.JobId);
        }
    }
}
