using System.ServiceModel;
using DataverseSyncWorker.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace DataverseSyncWorker.Services;

public sealed class DataverseWriter(DataverseConnection connection) : IDataverseWriter
{
    // Writer chỉ nhận Entity đã được mapper; không chứa business rule về source SQL.
    public Task WriteBuildings(IReadOnlyList<Entity> buildings, CancellationToken ct) => WriteStandard(buildings, ct);
    public Task WriteEquipment(IReadOnlyList<Entity> equipment, CancellationToken ct) => WriteStandard(equipment, ct);
    private async Task WriteStandard(IReadOnlyList<Entity> rows, CancellationToken ct)
    {
        // Building/Equipment/Point là standard table nên ghi từng Upsert request.
        foreach (var row in rows) await Execute(new UpsertRequest { Target = row }, ct);
    }
    public async Task WritePoints(IReadOnlyList<Entity> points, CancellationToken ct)
    {
        foreach (var point in points)
            await Execute(new UpsertRequest { Target = point }, ct);
    }

    public async Task WriteHistory(IReadOnlyList<Entity> readings, CancellationToken ct)
    {
        if (readings.Count == 0) return;
        // Elastic history hỗ trợ UpsertMultiple; deterministic GUID + partition giúp replay idempotent.
        await Execute(new OrganizationRequest("UpsertMultiple")
        {
            ["Targets"] = new EntityCollection(readings.ToList()) { EntityName = "fmc_bmsreading" }
        }, ct);
        // Elastic partial failures throw: caller deliberately does not acknowledge the SQL batch.
        // Replaying all rows is safe because each uses the same GUID and partition.
    }

    private async Task Execute(OrganizationRequest request, CancellationToken ct)
    {
        // Chỉ retry lỗi transient; lỗi schema/quyền phải nổi lên để không làm mất backlog SQL.
        for (var attempt = 0; ; attempt++)
        {
            try { await connection.Get().ExecuteAsync(request, ct); return; }
            catch (Exception ex) when (attempt < 4 && DataverseRetryPolicy.IsTransient(ex))
            {
                var delay = ex is FaultException<OrganizationServiceFault> fault &&
                    fault.Detail.ErrorDetails.TryGetValue("Retry-After", out var retry) && retry is TimeSpan serverDelay
                    ? serverDelay : TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
                await Task.Delay(delay, ct);
            }
        }
    }

}
