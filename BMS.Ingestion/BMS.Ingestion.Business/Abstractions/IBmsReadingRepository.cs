using BMS.Ingestion.Business.Contracts;

namespace BMS.Ingestion.Business.Abstractions;

/// <summary>Cổng persistence cho lịch sử reading BMS chỉ-append.</summary>
public interface IBmsReadingRepository
{
    /// <summary>Stage một reading chỉ-append trong Unit of Work hiện tại.</summary>
    void Add(BmsReadingDto reading);
}
