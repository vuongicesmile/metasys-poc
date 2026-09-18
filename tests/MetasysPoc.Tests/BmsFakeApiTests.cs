using BMS.Ingestion.Domain.Models;
using BMS.Fake.DataAccess.Services;
using BMS.Fake.Domain.Rules;

namespace MetasysPoc.Tests;

public sealed class BmsFakeApiTests
{
    [Theory]
    [InlineData("Temperature", 2, 0.2)]
    [InlineData("WaterConsumption", 15, 0.15)]
    public void Delta_policy_is_deterministic(string objectType, int roll, decimal expected)
    {
        Assert.Equal(expected, CovDeltaPolicy.Calculate(objectType, roll));
    }

    [Fact]
    public void Point_store_keeps_fixture_state_private_and_emits_cov_event()
    {
        var store = new MetasysPointStore();
        var original = store.Get("WATER-001");

        Assert.NotNull(original);

        var timestamp = DateTime.UtcNow;
        var covEvent = store.ChangeValue("WATER-001", 351.25m, timestamp);
        var current = store.Get("WATER-001");

        Assert.NotNull(current);
        Assert.Equal(350.00m, original!.Value);
        Assert.Equal(350.00m, covEvent.PreviousValue);
        Assert.Equal(351.25m, covEvent.CurrentValue);
        Assert.Equal(timestamp, covEvent.Timestamp);
        Assert.Equal(351.25m, current!.Value);
    }

    [Fact]
    public async Task Subscription_manager_publishes_only_to_matching_subscription()
    {
        var subscriptions = new SubscriptionManager();
        var response = subscriptions.Create(["WATER-001"]);
        var subscription = subscriptions.Get(response.SubscriptionId);

        Assert.NotNull(subscription);

        subscriptions.Publish(new CovEvent { ObjectId = "TEMP-001" });
        subscriptions.Publish(new CovEvent { ObjectId = "WATER-001", CurrentValue = 351.25m });

        var published = await subscription!.Events.Reader.ReadAsync();

        Assert.Equal("WATER-001", published.ObjectId);
        Assert.Equal(351.25m, published.CurrentValue);
        Assert.False(subscription.Events.Reader.TryRead(out _));
    }
}
