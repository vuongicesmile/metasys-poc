using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace SpoIngestion.Core;

public sealed record SpoInboxFileResult(
    Guid FileId,
    string FileName,
    string SourcePath,
    string ETag,
    string Status,
    int InputRows = 0,
    int Delivered = 0,
    int Skipped = 0,
    string? Error = null);

/// <summary>
/// Consumes SharePoint file versions already archived in fmc_spofile by Power Automate.
/// The archived ETag is the durable checkpoint: a version is imported at most once.
/// </summary>
public sealed class SpoDataverseInboxProcessor(
    ServiceClient client,
    SpoLocalProcessor processor,
    SpoIngestionOptions options)
{
    private const string FileTable = "fmc_spofile";
    private const string FileColumn = "fmc_file";
    private const int Archived = 789110002;
    private const int NotRequested = 789111000;
    private const int Processing = 789111001;
    private const int Imported = 789111002;
    private const int Failed = 789111003;
    private const int ConcurrencyVersionMismatch = -2147088254;
    private const int BlockSize = 4 * 1024 * 1024;

    public async Task<IReadOnlyList<SpoInboxFileResult>> ProcessOnce(
        int maxFiles,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (maxFiles is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxFiles), "maxFiles must be 1..100.");
        if (!client.IsReady)
            throw new InvalidOperationException("Dataverse client is not ready: " + client.LastError);

        var candidates = await ReadCandidates(utcNow, cancellationToken);
        var selected = candidates
            .Select(row => new { Row = row, Source = TryResolve(row.GetAttributeValue<string>("fmc_sharepointpath")) })
            .OrderBy(x => SourceOrder(x.Source))
            .ThenBy(x => x.Row.GetAttributeValue<DateTime?>("fmc_receivedat") ?? x.Row.GetAttributeValue<DateTime>("createdon"))
            .Take(maxFiles)
            .ToArray();

        var results = new List<SpoInboxFileResult>();
        foreach (var candidate in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProcessFile(candidate.Row, candidate.Source, utcNow, cancellationToken));
        }
        return results;
    }

    private async Task<IReadOnlyList<Entity>> ReadCandidates(DateTime utcNow, CancellationToken cancellationToken)
    {
        var query = new QueryExpression(FileTable)
        {
            ColumnSet = new ColumnSet(
                "fmc_filename", "fmc_sharepointpath", "fmc_etag", "fmc_importedetag",
                "fmc_filesize", "fmc_importstatus", "fmc_receivedat", "modifiedon", "versionnumber"),
            TopCount = 100
        };
        query.Criteria.AddCondition("fmc_status", ConditionOperator.Equal, Archived);
        query.Criteria.AddCondition("fmc_importstatus", ConditionOperator.In, NotRequested, Processing);
        query.Orders.Add(new OrderExpression("fmc_receivedat", OrderType.Ascending));
        var rows = (await client.RetrieveMultipleAsync(query, cancellationToken)).Entities;
        var staleBefore = utcNow.AddMinutes(-15);
        return rows.Where(row =>
        {
            var state = row.GetAttributeValue<OptionSetValue>("fmc_importstatus")?.Value ?? NotRequested;
            if (state == Processing && row.GetAttributeValue<DateTime>("modifiedon").ToUniversalTime() > staleBefore)
                return false;
            var etag = row.GetAttributeValue<string>("fmc_etag");
            var imported = row.GetAttributeValue<string>("fmc_importedetag");
            return !string.IsNullOrWhiteSpace(etag) && !string.Equals(etag, imported, StringComparison.Ordinal);
        }).ToArray();
    }

    private async Task<SpoInboxFileResult> ProcessFile(
        Entity row,
        SpoSourceDefinition? source,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var fileName = row.GetAttributeValue<string>("fmc_filename") ?? "(unnamed)";
        var sourcePath = row.GetAttributeValue<string>("fmc_sharepointpath") ?? "";
        var etag = row.GetAttributeValue<string>("fmc_etag") ?? "";

        if (source is null)
            return await Fail(row, fileName, sourcePath, etag,
                $"No enabled SPO mapping matches '{sourcePath}'.", cancellationToken);

        if (!await TryClaim(row, cancellationToken))
            return new(row.Id, fileName, sourcePath, etag, "SkippedConcurrency");

        try
        {
            var declaredSize = row.GetAttributeValue<int?>("fmc_filesize") ?? 0;
            if (declaredSize <= 0 || declaredSize > options.MaxFileBytes)
                throw new InvalidDataException($"Archived file size must be 1..{options.MaxFileBytes} bytes; actual={declaredSize}.");

            await using var content = await Download(row.Id, cancellationToken);
            if (content.Length != declaredSize)
                throw new InvalidDataException($"Archived file size mismatch; metadata={declaredSize}, downloaded={content.Length}.");

            var result = await processor.Process(content, sourcePath, utcNow, cancellationToken);
            if (!string.Equals(result.Status, "Completed", StringComparison.Ordinal))
            {
                var detail = string.Join(" | ", result.Issues.Take(10).Select(x => $"row {x.Ordinal} {x.Code}: {x.Message}"));
                return await FailClaimed(row.Id, fileName, sourcePath, etag,
                    string.IsNullOrWhiteSpace(detail) ? $"Import ended with status {result.Status}." : detail,
                    cancellationToken);
            }

            await client.UpdateAsync(new Entity(FileTable, row.Id)
            {
                ["fmc_importstatus"] = new OptionSetValue(Imported),
                ["fmc_importedetag"] = etag,
                ["fmc_rowcount"] = result.InputRows,
                ["fmc_processedat"] = utcNow,
                ["fmc_errormessage"] = null
            }, cancellationToken);
            return new(row.Id, fileName, sourcePath, etag, "Imported",
                result.InputRows, result.Delivered, result.Skipped);
        }
        catch (SpoDependencyException ex)
        {
            await client.UpdateAsync(new Entity(FileTable, row.Id)
            {
                ["fmc_importstatus"] = new OptionSetValue(NotRequested),
                ["fmc_errormessage"] = Limit(ex.Message)
            }, cancellationToken);
            return new(row.Id, fileName, sourcePath, etag, "WaitingDependency", Error: ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await FailClaimed(row.Id, fileName, sourcePath, etag, ex.Message, cancellationToken);
        }
    }

    private async Task<bool> TryClaim(Entity row, CancellationToken cancellationToken)
    {
        var claim = new Entity(FileTable, row.Id) { RowVersion = row.RowVersion };
        claim["fmc_importstatus"] = new OptionSetValue(Processing);
        claim["fmc_errormessage"] = null;
        try
        {
            await client.ExecuteAsync(new UpdateRequest
            {
                Target = claim,
                ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
            }, cancellationToken);
            return true;
        }
        catch (FaultException<OrganizationServiceFault> fault)
            when (fault.Detail.ErrorCode == ConcurrencyVersionMismatch)
        {
            return false;
        }
    }

    private async Task<MemoryStream> Download(Guid fileId, CancellationToken cancellationToken)
    {
        var initialize = (InitializeFileBlocksDownloadResponse)await client.ExecuteAsync(
            new InitializeFileBlocksDownloadRequest
            {
                Target = new EntityReference(FileTable, fileId),
                FileAttributeName = FileColumn
            }, cancellationToken);
        if (initialize.FileSizeInBytes <= 0 || initialize.FileSizeInBytes > options.MaxFileBytes)
            throw new InvalidDataException($"Dataverse file size must be 1..{options.MaxFileBytes} bytes; actual={initialize.FileSizeInBytes}.");

        var stream = new MemoryStream((int)initialize.FileSizeInBytes);
        for (long offset = 0; offset < initialize.FileSizeInBytes; offset += BlockSize)
        {
            var length = (int)Math.Min(BlockSize, initialize.FileSizeInBytes - offset);
            var block = (DownloadBlockResponse)await client.ExecuteAsync(new DownloadBlockRequest
            {
                FileContinuationToken = initialize.FileContinuationToken,
                Offset = offset,
                BlockLength = length
            }, cancellationToken);
            await stream.WriteAsync(block.Data, cancellationToken);
        }
        stream.Position = 0;
        return stream;
    }

    private async Task<SpoInboxFileResult> Fail(
        Entity row, string fileName, string sourcePath, string etag, string error, CancellationToken cancellationToken)
    {
        if (!await TryClaim(row, cancellationToken))
            return new(row.Id, fileName, sourcePath, etag, "SkippedConcurrency");
        return await FailClaimed(row.Id, fileName, sourcePath, etag, error, cancellationToken);
    }

    private async Task<SpoInboxFileResult> FailClaimed(
        Guid fileId, string fileName, string sourcePath, string etag, string error, CancellationToken cancellationToken)
    {
        await client.UpdateAsync(new Entity(FileTable, fileId)
        {
            ["fmc_importstatus"] = new OptionSetValue(Failed),
            ["fmc_rowcount"] = 0,
            ["fmc_processedat"] = DateTime.UtcNow,
            ["fmc_errormessage"] = Limit(error)
        }, cancellationToken);
        return new(fileId, fileName, sourcePath, etag, "Failed", Error: error);
    }

    private SpoSourceDefinition? TryResolve(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        try { return SpoConfiguration.Resolve(options, sourcePath); }
        catch (InvalidOperationException) { return null; }
    }

    private static int SourceOrder(SpoSourceDefinition? source) => source?.Mapping switch
    {
        "building-v1" => 0,
        "equipment-v1" or "water-meter-v1" => 1,
        _ => 2
    };

    private static string Limit(string value) => value.Length <= 3800 ? value : value[..3800];
}
