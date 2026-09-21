using BMS.Fake.Business.Contracts;
using BMS.Ingestion.Domain.Models;

namespace BMS.Fake.Business.Abstractions;

/// <summary>Cổng business để đọc và thay đổi state point trong Fake Metasys.</summary>
public interface IMetasysPointStore
{
    /// <summary>Đọc catalog tòa nhà từ database Fake.</summary>
    Task<IReadOnlyList<BmsBuildingDto>> GetBuildingsAsync(CancellationToken cancellationToken = default);

    /// <summary>Đọc catalog thiết bị từ database Fake.</summary>
    Task<IReadOnlyList<BmsEquipmentDto>> GetEquipmentAsync(CancellationToken cancellationToken = default);

    /// <summary>Đọc bản snapshot của mọi point hiện tại từ database.</summary>
    Task<IReadOnlyList<MetasysPointDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Tìm một point theo ObjectId.</summary>
    Task<MetasysPointDto?> GetAsync(string objectId, CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra ObjectId có tồn tại trong database hay không.</summary>
    Task<bool> ExistsAsync(string objectId, CancellationToken cancellationToken = default);

    /// <summary>Đổi giá trị point trong database và tạo shared CovEvent.</summary>
    Task<CovEvent> ChangeValueAsync(
        string objectId,
        decimal newValue,
        DateTime timestamp,
        CancellationToken cancellationToken = default);
}
