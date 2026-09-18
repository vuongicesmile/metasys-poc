using BMS.Ingestion.Domain.Models;
using BMS.Fake.Business.Abstractions;
using BMS.Fake.Common.Configuration;
using BMS.Fake.Domain.Rules;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BMS.Fake.Business.Services;

/// <summary>Application orchestration that generates COV events from point state.</summary>
public sealed class MetasysSimulator(
    IMetasysPointStore store,
    ISubscriptionManager subscriptions,
    SimulatorOptions options,
    ILogger<MetasysSimulator> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(Math.Max(1, options.IntervalSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var point in store.GetAll())
            {
                var roll = point.ObjectType == "Temperature"
                    ? Random.Shared.Next(-2, 3)
                    : Random.Shared.Next(5, 31);
                var delta = CovDeltaPolicy.Calculate(point.ObjectType, roll);

                // A zero temperature delta represents no COV.
                if (delta == 0) continue;

                var newValue = decimal.Round(point.Value + delta, 2);
                if (newValue == point.Value) continue;

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
