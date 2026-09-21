using System.ServiceModel;
using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.Contracts;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace DataverseSyncWorker.Services;

/// <summary>
/// Adapter Dataverse của tầng DataAccess.
/// Business chỉ gửi <see cref="DataverseRecord"/> thuần .NET; adapter này mới dùng SDK.
/// </summary>
public sealed class DataverseWriter(
    DataverseConnection connection,
    IIntegrationFailureClassifier failures) : IDataverseWriter
{
    public Task WriteBuildings(IReadOnlyList<DataverseRecord> buildings, CancellationToken ct) =>
        WriteStandard(buildings, ct);

    public Task WriteEquipment(IReadOnlyList<DataverseRecord> equipment, CancellationToken ct) =>
        WriteStandard(equipment, ct);

    private async Task WriteStandard(IReadOnlyList<DataverseRecord> rows, CancellationToken ct)
    {
        foreach (var row in rows)
            await Execute(new UpsertRequest { Target = ToEntity(row) }, ct);
    }

    public async Task WritePoints(IReadOnlyList<DataverseRecord> points, CancellationToken ct)
    {
        foreach (var point in points)
            await Execute(new UpsertRequest { Target = ToEntity(point) }, ct);
    }

    public async Task WriteHistory(IReadOnlyList<DataverseRecord> readings, CancellationToken ct)
    {
        if (readings.Count == 0) return;

        // Elastic history dùng deterministic GUID + partition nên replay vẫn idempotent.
        await Execute(new OrganizationRequest("UpsertMultiple")
        {
            ["Targets"] = new EntityCollection(readings.Select(ToEntity).ToList())
            {
                EntityName = "fmc_bmsreading"
            }
        }, ct);
    }

    /// <summary>Chuyển contract độc lập SDK thành Entity tại đúng boundary Infrastructure.</summary>
    private static Entity ToEntity(DataverseRecord record)
    {
        var entity = new Entity(record.LogicalName, record.Id);
        foreach (var (name, value) in record.Attributes)
        {
            entity[name] = value switch
            {
                DataverseReference reference => new EntityReference(reference.LogicalName, reference.Id),
                DataverseChoice choice => new OptionSetValue(choice.Value),
                _ => value
            };
        }
        return entity;
    }

    private async Task Execute(OrganizationRequest request, CancellationToken ct)
    {
        // Chỉ retry lỗi transient; lỗi schema/quyền phải nổi lên để không mất backlog SQL.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await connection.Get().ExecuteAsync(request, ct);
                return;
            }
            catch (Exception ex) when (attempt < 4 && failures.IsTransient(ex))
            {
                var delay = ex is FaultException<OrganizationServiceFault> fault &&
                    fault.Detail.ErrorDetails.TryGetValue("Retry-After", out var retry) &&
                    retry is TimeSpan serverDelay
                    ? serverDelay
                    : TimeSpan.FromSeconds(Math.Pow(2, attempt) + Random.Shared.NextDouble());
                await Task.Delay(delay, ct);
            }
        }
    }
}
