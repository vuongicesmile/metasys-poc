using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Abstractions;

/// <summary>
/// Đọc catalog BMS từ SQL Server.
///
/// Catalog được tách khỏi delivery ledger để người đọc có thể phân biệt rõ:
/// catalog là dữ liệu tham chiếu (building/equipment), còn ledger là trạng thái
/// giao từng reading sang Dataverse.
/// </summary>
public interface ISqlCatalogReader
{
    Task<(List<BmsBuilding> Buildings, List<BmsEquipment> Equipment)> Read(CancellationToken ct);
}