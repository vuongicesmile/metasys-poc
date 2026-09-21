using System.Threading.Channels;
using BMS.Ingestion.Domain.Models;

namespace BMS.Fake.Business.Models;

/// <summary>Runtime state của subscription được SSE presentation adapter đọc.</summary>
public sealed class MetasysSubscription
{
    // ID public mà client dùng để mở stream.
    public required string Id { get; init; }

    // Các ObjectId mà subscription đang theo dõi.
    public required HashSet<string> ObjectIds { get; init; }

    // Channel là hàng đợi event giữa simulator và HTTP SSE endpoint.
    public Channel<CovEvent> Events { get; } = Channel.CreateUnbounded<CovEvent>(
        // Một reader là SSE connection; nhiều writer có thể là simulator/request.
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
}
