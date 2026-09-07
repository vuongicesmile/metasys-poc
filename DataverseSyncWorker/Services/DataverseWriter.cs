using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace DataverseSyncWorker.Services;

public interface IDataverseWriter
{
    Task WritePoints(IReadOnlyList<Entity> points, CancellationToken ct);
    Task WriteHistory(IReadOnlyList<Entity> readings, CancellationToken ct);
}

public sealed class DataverseWriter(DataverseConnection connection) : IDataverseWriter
{
    public async Task WritePoints(IReadOnlyList<Entity> points, CancellationToken ct)
    {
        foreach (var point in points)
            await Execute(new UpsertRequest { Target = point }, ct);
    }

    public async Task WriteHistory(IReadOnlyList<Entity> readings, CancellationToken ct)
    {
        if (readings.Count == 0) return;
        await Execute(new OrganizationRequest("UpsertMultiple")
        {
            ["Targets"] = new EntityCollection(readings.ToList()) { EntityName = "fmc_bmsreading" }
        }, ct);
        // Elastic partial failures throw: caller deliberately does not acknowledge the SQL batch.
        // Replaying all rows is safe because each uses the same GUID and partition.
    }

    private async Task Execute(OrganizationRequest request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { await connection.Get().ExecuteAsync(request, ct); return; }
            catch (Exception ex) when (attempt < 4 && IsTransient(ex))
            {
                var delay = ex is FaultException<OrganizationServiceFault> fault &&
                    fault.Detail.ErrorDetails.TryGetValue("Retry-After", out var retry) && retry is TimeSpan serverDelay
                    ? serverDelay : TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
                await Task.Delay(delay, ct);
            }
        }
    }

    internal static bool IsTransient(Exception ex) => ex is HttpRequestException or TimeoutException ||
        ex is FaultException<OrganizationServiceFault> f &&
            (f.Detail.ErrorDetails.Contains("Retry-After") ||
             f.Detail.ErrorCode is -2147015902 or -2147015903 or -2147015898);
}
