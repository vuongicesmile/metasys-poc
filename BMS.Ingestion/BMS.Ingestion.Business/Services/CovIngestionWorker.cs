using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BMS.Ingestion.Business.Services;

/// <summary>Application orchestration: catalog -> optional SQL -> subscribe -> COV -> optional SQL.</summary>
public sealed class CovIngestionWorker(
    AppSettings settings,
    IngestionRuntimeOptions runtimeOptions,
    IngestionStatusTracker status,
    IMetasysClient metasys,
    IBmsReadingRepository repository,
    ILogger<CovIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        status.Configure(settings.Metasys.BaseUrl, runtimeOptions.SqlEnabled);

        try
        {
            logger.LogInformation("Connecting to Fake Metasys at {BaseUrl}", settings.Metasys.BaseUrl);
            var catalog = await metasys.ReadCatalogAsync(stoppingToken);
            if (runtimeOptions.SqlEnabled)
            {
                await repository.PersistCatalogAsync(catalog.Buildings, catalog.Equipment, stoppingToken);
                status.CatalogPersisted(catalog.Buildings.Length, catalog.Equipment.Length);
            }

            var subscription = await metasys.SubscribeAsync(
                catalog.Points.Select(point => point.ObjectId), stoppingToken);
            status.Connected(subscription.SubscriptionId);
            logger.LogInformation(
                "Created {SubscriptionId}; SQL persistence enabled: {SqlEnabled}",
                subscription.SubscriptionId,
                runtimeOptions.SqlEnabled);

            await foreach (var covEvent in metasys.ReadEventsAsync(subscription.SubscriptionId, stoppingToken))
            {
                status.EventReceived(covEvent);
                logger.LogInformation(
                    "COV {ObjectId}: {PreviousValue} -> {CurrentValue} {Unit}",
                    covEvent.ObjectId,
                    covEvent.PreviousValue,
                    covEvent.CurrentValue,
                    covEvent.Unit);

                if (runtimeOptions.SqlEnabled)
                {
                    await repository.InsertAsync(covEvent, stoppingToken);
                    status.RowInserted();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            status.Failed(exception);
            logger.LogError(exception, "COV ingestion stopped with an error.");
        }
    }
}
