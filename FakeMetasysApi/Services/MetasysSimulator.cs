namespace FakeMetasysApi.Services;

public sealed class MetasysSimulator(
    MetasysPointStore store,
    SubscriptionManager subscriptions,
    IConfiguration configuration,
    ILogger<MetasysSimulator> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(
        Math.Max(1, configuration.GetValue("Simulator:IntervalSeconds", 3)));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var point in store.GetAll())
            {
                var delta = point.ObjectType == "Temperature"
                    ? Random.Shared.Next(-2, 3) / 10m
                    : Random.Shared.Next(5, 31) / 100m;

                // A zero temperature delta represents no COV and therefore emits no event.
                if (delta == 0)
                {
                    continue;
                }

                var newValue = decimal.Round(point.Value + delta, 2);
                if (newValue == point.Value)
                {
                    continue;
                }

                var covEvent = store.ChangeValue(point.ObjectId, newValue, DateTime.UtcNow);
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
