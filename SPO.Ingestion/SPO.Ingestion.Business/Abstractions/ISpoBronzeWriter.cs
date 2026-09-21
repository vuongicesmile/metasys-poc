using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business.Abstractions;

/// <summary>
/// Cổng ghi typed Bronze records vào Dataverse.
/// Business không phụ thuộc trực tiếp vào ServiceClient hay QueryExpression.
/// </summary>
public interface ISpoBronzeWriter
{
    Task<BronzeWriteResult> Write(IReadOnlyList<BronzeRecord> records, CancellationToken cancellationToken);
}
