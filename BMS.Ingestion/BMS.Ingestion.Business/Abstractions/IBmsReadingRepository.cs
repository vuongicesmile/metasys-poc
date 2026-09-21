using BMS.Ingestion.Business.Contracts;

namespace BMS.Ingestion.Business.Abstractions;

/// <summary>Cổng persistence cho lịch sử reading BMS chỉ-append.</summary>
public interface IBmsReadingRepository
{
    /// <summary>Append một reading đã được chuẩn bị vào bảng lịch sử raw.</summary>
    Task InsertAsync(BmsReadingDto reading, CancellationToken cancellationToken = default);
}
