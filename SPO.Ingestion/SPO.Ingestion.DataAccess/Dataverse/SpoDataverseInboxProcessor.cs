using System.ServiceModel;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using SPO.Ingestion.Business;
using SPO.Ingestion.Common;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.DataAccess;

/// <summary>
/// Đọc file đã archive trong Dataverse và giao nội dung cho Application xử lý.
/// Class nằm ở DataAccess vì trực tiếp dùng ServiceClient và Microsoft.Xrm.Sdk.
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
    private const int ChangeDispatched = 789112002;
    private const int ChangeFailed = 789112004;
    private const int ChangeImported = 789112005;
    private const string ChangeTable = "fmc_spochangerequest";
    private const int ConcurrencyVersionMismatch = -2147088254;
    private const int BlockSize = 4 * 1024 * 1024;

    public async Task<IReadOnlyList<SpoInboxFileResult>> ProcessOnce(
        int maxFiles,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Giới hạn số file giúp một vòng xử lý không giữ worker quá lâu.
        if (maxFiles is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(maxFiles), "maxFiles must be 1..100.");
        if (!client.IsReady)
            throw new InvalidOperationException("Dataverse client is not ready: " + client.LastError);

        // Đọc file có thể xử lý, xác định mapping rồi ưu tiên catalog trước reading.
        var candidates = await ReadCandidates(utcNow, cancellationToken);
        var selected = candidates
            .Select(row => new
            {
                Row = row,
                Source = TryResolve(row.GetAttributeValue<string>("fmc_sharepointpath"))
            })
            .OrderBy(x => SourceOrder(x.Source))
            .ThenBy(x => x.Row.GetAttributeValue<DateTime?>("fmc_receivedat") ??
                         x.Row.GetAttributeValue<DateTime>("createdon"))
            .Take(maxFiles)
            .ToArray();

        var results = new List<SpoInboxFileResult>();
        foreach (var candidate in selected)
        {
            // Tôn trọng yêu cầu dừng trước khi bắt đầu file tiếp theo.
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ProcessFile(candidate.Row, candidate.Source, utcNow, cancellationToken));
        }
        return results;
    }

    private async Task<IReadOnlyList<Entity>> ReadCandidates(
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Chỉ lấy metadata cần thiết; nội dung file được tải riêng sau khi claim thành công.
        var query = new QueryExpression(FileTable)
        {
            ColumnSet = new ColumnSet(
                "fmc_filename", "fmc_sharepointpath", "fmc_sourcekey", "fmc_etag", "fmc_importedetag",
                "fmc_filesize", "fmc_importstatus", "fmc_receivedat", "modifiedon", "versionnumber"),
            TopCount = 100
        };
        query.Criteria.AddCondition("fmc_status", ConditionOperator.Equal, Archived);
        query.Criteria.AddCondition("fmc_importstatus", ConditionOperator.In, NotRequested, Processing);
        query.Orders.Add(new OrderExpression("fmc_receivedat", OrderType.Ascending));
        var rows = (await client.RetrieveMultipleAsync(query, cancellationToken)).Entities;
        // Processing quá 15 phút được xem là lease cũ để worker khác có thể recovery.
        var staleBefore = utcNow.AddMinutes(-15);
        return rows.Where(row =>
        {
            var state = row.GetAttributeValue<OptionSetValue>("fmc_importstatus")?.Value ?? NotRequested;
            if (state == Processing &&
                row.GetAttributeValue<DateTime>("modifiedon").ToUniversalTime() > staleBefore)
                return false;
            var etag = row.GetAttributeValue<string>("fmc_etag");
            var imported = row.GetAttributeValue<string>("fmc_importedetag");
            return !string.IsNullOrWhiteSpace(etag) &&
                   !string.Equals(etag, imported, StringComparison.Ordinal);
        }).ToArray();
    }

    private async Task<SpoInboxFileResult> ProcessFile(
        Entity row,
        SpoSourceDefinition? source,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        // Lấy thông tin nhận diện trước để mọi nhánh kết quả đều có đủ receipt.
        var fileName = row.GetAttributeValue<string>("fmc_filename") ?? "(unnamed)";
        var sourcePath = row.GetAttributeValue<string>("fmc_sharepointpath") ?? "";
        var sourceKey = row.GetAttributeValue<string>("fmc_sourcekey") ?? "";
        var etag = row.GetAttributeValue<string>("fmc_etag") ?? "";

        if (source is null)
            return await Fail(
                row,
                fileName,
                sourcePath,
                etag,
                $"No enabled SPO mapping matches '{sourcePath}'.",
                cancellationToken);

        // Claim bằng RowVersion; chỉ một worker được quyền xử lý phiên bản file này.
        if (!await TryClaim(row, cancellationToken))
            return new(row.Id, fileName, sourcePath, etag, "SkippedConcurrency");

        try
        {
            // So sánh kích thước metadata và file thật để phát hiện archive thiếu/hỏng.
            var declaredSize = row.GetAttributeValue<int?>("fmc_filesize") ?? 0;
            if (declaredSize <= 0 || declaredSize > options.MaxFileBytes)
                throw new InvalidDataException(
                    $"Archived file size must be 1..{options.MaxFileBytes} bytes; actual={declaredSize}.");

            await using var content = await Download(row.Id, cancellationToken);
            if (content.Length != declaredSize)
                throw new InvalidDataException(
                    $"Archived file size mismatch; metadata={declaredSize}, downloaded={content.Length}.");

            // Application xử lý parse, validate, map và ghi typed records.
            var result = await processor.Process(content, sourcePath, utcNow, cancellationToken);
            if (!string.Equals(result.Status, "Completed", StringComparison.Ordinal))
            {
                var detail = string.Join(
                    " | ",
                    result.Issues.Take(10).Select(x => $"row {x.Ordinal} {x.Code}: {x.Message}"));
                return await FailClaimed(
                    row.Id,
                    fileName,
                    sourcePath,
                    sourceKey,
                    etag,
                    string.IsNullOrWhiteSpace(detail)
                        ? $"Import ended with status {result.Status}."
                        : detail,
                    cancellationToken);
            }

            // Chỉ đánh dấu Imported sau khi toàn bộ pipeline hoàn tất.
            await client.UpdateAsync(new Entity(FileTable, row.Id)
            {
                ["fmc_importstatus"] = new OptionSetValue(Imported),
                ["fmc_importedetag"] = etag,
                ["fmc_rowcount"] = result.InputRows,
                ["fmc_processedat"] = utcNow,
                ["fmc_errormessage"] = null
            }, cancellationToken);
            await MarkChangeRequests(sourceKey, etag, ChangeImported, null, cancellationToken);
            return new(
                row.Id,
                fileName,
                sourcePath,
                etag,
                "Imported",
                result.InputRows,
                result.Delivered,
                result.Skipped);
        }
        catch (SpoDependencyException ex)
        {
            // Thiếu building/equipment cha là lỗi có thể thử lại sau, nên trả về NotRequested.
            await client.UpdateAsync(new Entity(FileTable, row.Id)
            {
                ["fmc_importstatus"] = new OptionSetValue(NotRequested),
                ["fmc_errormessage"] = Limit(ex.Message)
            }, cancellationToken);
            return new(row.Id, fileName, sourcePath, etag, "WaitingDependency", Error: ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Lỗi dữ liệu hoặc lỗi cố định được lưu vào trạng thái Failed để vận hành kiểm tra.
            return await FailClaimed(
                row.Id,
                fileName,
                sourcePath,
                sourceKey,
                etag,
                ex.Message,
                cancellationToken);
        }
    }

    private async Task<bool> TryClaim(Entity row, CancellationToken cancellationToken)
    {
        // Gửi RowVersion hiện tại để Dataverse từ chối nếu worker khác đã sửa bản ghi trước.
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
        // Dataverse file column cần khởi tạo download session trước khi đọc từng block.
        var initialize = (InitializeFileBlocksDownloadResponse)await client.ExecuteAsync(
            new InitializeFileBlocksDownloadRequest
            {
                Target = new EntityReference(FileTable, fileId),
                FileAttributeName = FileColumn
            }, cancellationToken);
        if (initialize.FileSizeInBytes <= 0 || initialize.FileSizeInBytes > options.MaxFileBytes)
            throw new InvalidDataException(
                $"Dataverse file size must be 1..{options.MaxFileBytes} bytes; actual={initialize.FileSizeInBytes}.");

        var stream = new MemoryStream((int)initialize.FileSizeInBytes);
        for (long offset = 0; offset < initialize.FileSizeInBytes; offset += BlockSize)
        {
            // Đọc theo block 4 MB để không yêu cầu một response quá lớn từ Dataverse.
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
        Entity row,
        string fileName,
        string sourcePath,
        string etag,
        string error,
        CancellationToken cancellationToken)
    {
        if (!await TryClaim(row, cancellationToken))
            return new(row.Id, fileName, sourcePath, etag, "SkippedConcurrency");
        return await FailClaimed(
            row.Id,
            fileName,
            sourcePath,
            row.GetAttributeValue<string>("fmc_sourcekey") ?? "",
            etag,
            error,
            cancellationToken);
    }

    private async Task<SpoInboxFileResult> FailClaimed(
        Guid fileId,
        string fileName,
        string sourcePath,
        string sourceKey,
        string etag,
        string error,
        CancellationToken cancellationToken)
    {
        // Giới hạn error text để không vượt độ dài cột fmc_errormessage.
        await client.UpdateAsync(new Entity(FileTable, fileId)
        {
            ["fmc_importstatus"] = new OptionSetValue(Failed),
            ["fmc_rowcount"] = 0,
            ["fmc_processedat"] = DateTime.UtcNow,
            ["fmc_errormessage"] = Limit(error)
        }, cancellationToken);
        await MarkChangeRequests(sourceKey, etag, ChangeFailed, error, cancellationToken);
        return new(fileId, fileName, sourcePath, etag, "Failed", Error: error);
    }

    private async Task MarkChangeRequests(string sourceKey, string etag, int status, string? error,
        CancellationToken cancellationToken)
    {
        // A queue record is complete only after this worker has imported the
        // exact archived ETag. A newer request remains Pending/Dispatched.
        if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(etag)) return;
        var query = new QueryExpression(ChangeTable)
        {
            ColumnSet = new ColumnSet("fmc_spochangerequestid"),
            TopCount = 10
        };
        query.Criteria.AddCondition("fmc_sourcekey", ConditionOperator.Equal, sourceKey);
        query.Criteria.AddCondition("fmc_expectedetag", ConditionOperator.Equal, etag);
        query.Criteria.AddCondition("fmc_status", ConditionOperator.Equal, ChangeDispatched);
        foreach (var request in (await client.RetrieveMultipleAsync(query, cancellationToken)).Entities)
        {
            await client.UpdateAsync(new Entity(ChangeTable, request.Id)
            {
                ["fmc_status"] = new OptionSetValue(status),
                ["fmc_errormessage"] = error is null ? null : Limit(error)
            }, cancellationToken);
        }
    }

    private SpoSourceDefinition? TryResolve(string? sourcePath)
    {
        // Path không khớp mapping được trả về null để caller ghi trạng thái Failed có kiểm soát.
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        try
        {
            return SpoConfiguration.Resolve(options, sourcePath);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static int SourceOrder(SpoSourceDefinition? source) => source?.Mapping switch
    {
        // Catalog cha phải chạy trước catalog con và reading để lookup luôn sẵn sàng.
        "building-v1" => 0,
        "equipment-v1" or "water-meter-v1" or "electric-meter-v1" => 1,
        _ => 2
    };

    private static string Limit(string value) => value.Length <= 3800 ? value : value[..3800];
}
