using System.Collections.Concurrent;
using BMS.Fake.Business.Contracts;
using BMS.Fake.Business.Abstractions;
using BMS.Fake.Business.Models;
using BMS.Ingestion.Domain.Models;

namespace BMS.Fake.DataAccess.Services;

/// <summary>Adapter subscription in-memory sử dụng Channel để truyền event.</summary>
public sealed class SubscriptionManager : ISubscriptionManager
{
    // Dictionary thread-safe lưu các subscription đang hoạt động.
    private readonly ConcurrentDictionary<string, MetasysSubscription> _subscriptions =
        new(StringComparer.OrdinalIgnoreCase);
    // Counter tạo ID tuần tự dễ đọc trong môi trường Fake.
    private int _nextId;

    /// <summary>Tạo subscription mới và chuẩn hóa danh sách ObjectId.</summary>
    public SubscriptionResponseDto Create(IEnumerable<string> objectIds)
    {
        // Interlocked bảo đảm nhiều request đồng thời không trùng số ID.
        var id = $"SUB-{Interlocked.Increment(ref _nextId):D3}";
        // Lưu ObjectId dạng HashSet để kiểm tra Contains nhanh.
        var subscription = new MetasysSubscription
        {
            Id = id,
            ObjectIds = objectIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

        // Đăng ký subscription trước khi trả response cho caller.
        _subscriptions[id] = subscription;
        // Response chỉ chứa ID, client dùng ID này để mở SSE stream.
        return new SubscriptionResponseDto { SubscriptionId = id };
    }

    /// <summary>Tìm subscription; trả null nếu ID không tồn tại.</summary>
    public MetasysSubscription? Get(string id) =>
        _subscriptions.TryGetValue(id, out var subscription) ? subscription : null;

    /// <summary>Gửi event vào channel của subscription có theo dõi ObjectId.</summary>
    public void Publish(CovEvent covEvent)
    {
        // Duyệt các subscription hiện có.
        foreach (var subscription in _subscriptions.Values)
        {
            // Chỉ gửi event tới subscriber đã đăng ký đúng point.
            if (subscription.ObjectIds.Contains(covEvent.ObjectId))
                subscription.Events.Writer.TryWrite(covEvent);
        }
    }
}
