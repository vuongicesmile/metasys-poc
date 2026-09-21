using BMS.Fake.Business.Contracts;
using BMS.Fake.Business.Models;
using BMS.Ingestion.Domain.Models;

namespace BMS.Fake.Business.Abstractions;

/// <summary>Cổng business để tạo subscription và phát COV event tới subscribers.</summary>
public interface ISubscriptionManager
{
    /// <summary>Tạo subscription cho danh sách ObjectId.</summary>
    SubscriptionResponseDto Create(IEnumerable<string> objectIds);

    /// <summary>Tìm runtime subscription theo ID.</summary>
    MetasysSubscription? Get(string id);

    /// <summary>Phát event tới các subscription đang theo dõi ObjectId đó.</summary>
    void Publish(CovEvent covEvent);
}
