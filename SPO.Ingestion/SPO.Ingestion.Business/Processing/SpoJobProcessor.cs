using SPO.Ingestion.Business.Abstractions;
using SPO.Ingestion.Common;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business;

public sealed class SpoJobProcessor(
    SpoIngestionOptions options,
    ISpoJobStore store,
    ISpoJobUnitOfWorkFactory sessions,
    TabularParser parser,
    SpoBronzeMapper mapper,
    SpoRecordValidator validator,
    ISpoBronzeWriter writer)
{
    public async Task<SpoJobManifest> Process(string jobId, CancellationToken ct)
    {
        await store.Initialize(ct);
        await using var session = await sessions.TryCreate(jobId, ct);
        if (session is null) return await store.Read(jobId, ct);
        var job = await store.Read(jobId, ct);
        if (job.Status == "Completed") return job;
        job = job with { Status = "Processing", Attempt = job.Attempt + 1, Error = null };
        await session.Save(job, ct);
        try
        {
            var source = SpoConfiguration.Resolve(options, job.SourcePath);
            await using var content = await store.OpenRaw(job, ct);
            var rows = parser.Parse(content, Path.GetExtension(job.SourcePath), source);
            var now = DateTime.UtcNow;
            var validation = validator.Validate(source, rows, now);
            if (validation.Issues.Count > 0)
            {
                var first = validation.Issues[0];
                throw new InvalidDataException($"Validation failed for {validation.Issues.Count} row(s). First: row {first.Ordinal}, {first.Code}: {first.Message}");
            }

            var delivered = 0;
            var skipped = validation.ExpiredHistory;
            if (source.Mapping.EndsWith("reading-v1", StringComparison.Ordinal))
            {
                var latest = new Dictionary<string, BronzeRecord>(StringComparer.OrdinalIgnoreCase);
                foreach (var chunk in rows.Chunk(1000))
                foreach (var point in mapper.Map(source, chunk, now).Records.Where(x => x.Kind == BronzeRecordKind.Point))
                    if (!latest.TryGetValue(point.Identity, out var current) || point.EventTimeUtc > current.EventTimeUtc ||
                        point.EventTimeUtc == current.EventTimeUtc && point.SourceOrdinal > current.SourceOrdinal)
                        latest[point.Identity] = point;
                var pointResult = await writer.Write(latest.Values.ToArray(), ct);
                await session.SaveReceiptSegment("points", pointResult.Receipts, ct);
                delivered += pointResult.Delivered; skipped += pointResult.Skipped;
                var segment = 0;
                var deliveredEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var chunk in rows.Chunk(1000))
                {
                    var duplicateReceipts = new List<RowReceipt>();
                    var history = mapper.Map(source, chunk, now).Records.Where(x => x.Kind == BronzeRecordKind.History)
                        .Where(x =>
                        {
                            if (deliveredEvents.Add(x.Identity)) return true;
                            duplicateReceipts.Add(new(x.SourceOrdinal, x.Identity, "fmc_bmsreading", "SkippedDuplicate"));
                            return false;
                        }).ToArray();
                    if (history.Length == 0 && duplicateReceipts.Count == 0) continue;
                    var result = await writer.Write(history, ct);
                    await session.SaveReceiptSegment($"history-{segment++:D6}",
                        result.Receipts.Concat(duplicateReceipts).ToArray(), ct);
                    delivered += result.Delivered; skipped += result.Skipped + duplicateReceipts.Count;
                }
            }
            else
            {
                var mapped = mapper.Map(source, rows, now);
                var result = await writer.Write(mapped.Records, ct);
                await session.SaveReceipts(result.Receipts, ct);
                delivered = result.Delivered; skipped += result.Skipped;
            }
            job = job with { Status = "Completed", InputRows = rows.Count, Delivered = delivered,
                Skipped = skipped, Error = null };
            await session.Save(job, ct);
            return job;
        }
        catch (InvalidDataException ex)
        {
            job = job with { Status = "Failed", Error = ex.Message };
            await session.Save(job, ct);
            return job;
        }
        catch (SpoDependencyException ex)
        {
            job = job with { Status = job.Attempt < 12 ? "WaitingDependency" : "Failed", Error = ex.Message };
            await session.Save(job, ct);
            return job;
        }
        catch (Exception ex)
        {
            job = job with { Status = job.Attempt < 5 ? "Retrying" : "Failed", Error = ex.Message };
            await session.Save(job, ct);
            throw;
        }
    }
}
