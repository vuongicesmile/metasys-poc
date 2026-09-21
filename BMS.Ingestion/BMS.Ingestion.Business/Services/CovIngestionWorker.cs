using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Business.Mapping;
using BMS.Ingestion.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BMS.Ingestion.Business.Services;

/// <summary>
/// Điều phối vòng đời ingestion: đọc catalog → lưu catalog → subscribe → nhận COV → lưu reading.
/// SQL có thể tắt bằng runtime option để chạy smoke test không cần database.
/// </summary>
public sealed class CovIngestionWorker(
    // Cấu hình ứng dụng và URL của Fake Metasys.
    AppSettings settings,
    // Quyết định có ghi SQL hay chỉ quan sát event.
    IngestionRuntimeOptions runtimeOptions,
    // Theo dõi trạng thái cho endpoint status.
    IngestionStatusTracker status,
    // Client gọi API Metasys/Fake Metasys.
    IMetasysClient metasys,
    // Factory tạo persistence boundary ngắn hạn cho từng lần ghi nguyên tử.
    IBmsIngestionUnitOfWorkFactory unitOfWorkFactory,
    // Logger của worker.
    ILogger<CovIngestionWorker> logger) : BackgroundService
{
    // BackgroundService gọi method này khi host khởi động.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ghi nhận URL và trạng thái SQL ban đầu.
        status.Configure(settings.Metasys.BaseUrl, runtimeOptions.SqlEnabled);

        try
        {
            // Thông báo bắt đầu kết nối tới source.
            logger.LogInformation("Connecting to Fake Metasys at {BaseUrl}", settings.Metasys.BaseUrl);
            // Đọc danh sách building, equipment và point.
            var catalog = await metasys.ReadCatalogAsync(stoppingToken);
            if (runtimeOptions.SqlEnabled)
            {
                await using var unitOfWork = await unitOfWorkFactory.CreateAsync(stoppingToken);
                // Stage toàn bộ catalog rồi commit bằng cùng một EF Core Unit of Work.
                await unitOfWork.Catalog.StageAsync(catalog.ToPersistenceDto(), stoppingToken);
                await unitOfWork.CommitAsync(stoppingToken);
                // Cập nhật status sau khi catalog đã ghi thành công.
                status.CatalogPersisted(catalog.Buildings.Length, catalog.Equipment.Length);
            }

            // Đăng ký stream COV cho toàn bộ point trong catalog.
            var subscription = await metasys.SubscribeAsync(
                catalog.Points.Select(point => point.ObjectId), stoppingToken);
            // Đánh dấu worker đã kết nối thành công.
            status.Connected(subscription.SubscriptionId);
            logger.LogInformation(
                "Created {SubscriptionId}; SQL persistence enabled: {SqlEnabled}",
                subscription.SubscriptionId,
                runtimeOptions.SqlEnabled);

            // Đọc event liên tục cho tới khi app bị hủy hoặc source đóng stream.
            await foreach (var covEvent in metasys.ReadEventsAsync(subscription.SubscriptionId, stoppingToken))
            {
                // Ghi nhận event ngay cả khi SQL bị tắt.
                status.EventReceived(covEvent);
                logger.LogInformation(
                    "COV {ObjectId}: {PreviousValue} -> {CurrentValue} {Unit}",
                    covEvent.ObjectId,
                    covEvent.PreviousValue,
                    covEvent.CurrentValue,
                    covEvent.Unit);

                if (runtimeOptions.SqlEnabled)
                {
                    await using var unitOfWork = await unitOfWorkFactory.CreateAsync(stoppingToken);
                    // Mỗi COV được commit độc lập để event lỗi không bị ghi nhận thành công.
                    unitOfWork.Readings.Add(covEvent.ToPersistenceDto());
                    await unitOfWork.CommitAsync(stoppingToken);
                    // Chỉ tăng RowsInserted sau khi commit thành công.
                    status.RowInserted();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Đây là shutdown bình thường, không đánh dấu worker Failed.
        }
        catch (Exception exception)
        {
            // Giữ lỗi hiển thị trong status và log; không nuốt exception âm thầm.
            status.Failed(exception);
            logger.LogError(exception, "COV ingestion stopped with an error.");
        }
    }
}
