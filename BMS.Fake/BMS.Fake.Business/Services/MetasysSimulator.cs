using BMS.Ingestion.Domain.Models;
using BMS.Fake.Business.Abstractions;
using BMS.Fake.Common.Configuration;
using BMS.Fake.Domain.Rules;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BMS.Fake.Business.Services;

/// <summary>Điều phối simulator: thay đổi point state và phát COV event.</summary>
public sealed class MetasysSimulator(
    // Store giữ state point trong memory.
    IMetasysPointStore store,
    // Manager phân phối event tới subscription.
    ISubscriptionManager subscriptions,
    // Cấu hình khoảng thời gian giữa các lần thay đổi.
    SimulatorOptions options,
    // Logger ghi lại event đã phát.
    ILogger<MetasysSimulator> logger) : BackgroundService
{
    // Không cho interval nhỏ hơn một giây để simulator không tạo quá nhiều event.
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(Math.Max(1, options.IntervalSeconds));

    // Host gọi method này khi Fake Metasys khởi động.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer phát một tick theo interval đã cấu hình.
        using var timer = new PeriodicTimer(_interval);

        // Vòng lặp kết thúc khi host yêu cầu cancellation.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Lấy bản copy của point để tránh sửa trực tiếp collection khi đang duyệt.
            // Đọc snapshot point mới nhất từ database ở mỗi tick.
            foreach (var point in await store.GetAllAsync(stoppingToken))
            {
                // Temperature dùng roll nhỏ; loại khác dùng roll lớn hơn.
                var roll = point.ObjectType == "Temperature"
                    ? Random.Shared.Next(-2, 3)
                    : Random.Shared.Next(5, 31);
                // Policy đổi roll thành delta theo loại point.
                var delta = CovDeltaPolicy.Calculate(point.ObjectType, roll);

                // Delta bằng 0 nghĩa là không có thay đổi cần phát COV.
                if (delta == 0) continue;

                // Làm tròn giá trị mới để simulator ổn định theo hai chữ số thập phân.
                var newValue = decimal.Round(point.Value + delta, 2);
                // Nếu làm tròn làm mất thay đổi thì bỏ qua event.
                if (newValue == point.Value) continue;

                // Store cập nhật state và tạo shared CovEvent.
                // Ghi state mới vào database rồi tạo event từ previous/current value.
                var covEvent = await store.ChangeValueAsync(
                    point.ObjectId,
                    newValue,
                    DateTime.UtcNow,
                    stoppingToken);
                // Publish event tới đúng các subscription đang theo dõi point.
                subscriptions.Publish(covEvent);
                logger.LogInformation(
                    "COV {ObjectId}: {PreviousValue} -> {CurrentValue} {Unit}",
                    covEvent.ObjectId,
                    covEvent.PreviousValue,
                    covEvent.CurrentValue,
                    covEvent.Unit);
            }
        }
    }
}
