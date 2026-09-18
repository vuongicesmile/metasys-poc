using System.Threading.Channels;
using BMS.Ingestion.Domain.Models;

namespace BMS.Fake.Business.Models;

/// <summary>Runtime subscription state exposed to the SSE presentation adapter.</summary>
public sealed class MetasysSubscription
{
    public required string Id { get; init; }
    public required HashSet<string> ObjectIds { get; init; }
    public Channel<CovEvent> Events { get; } = Channel.CreateUnbounded<CovEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
}
