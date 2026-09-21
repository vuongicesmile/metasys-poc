using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business.Abstractions;

public interface ISpoBronzeWriter
{
    Task<BronzeWriteResult> Write(IReadOnlyList<BronzeRecord> records, CancellationToken cancellationToken);
}
