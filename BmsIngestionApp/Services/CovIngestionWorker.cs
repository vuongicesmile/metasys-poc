using System.Net.Http.Json;
using System.Text.Json;
using BmsIngestionApp.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BmsIngestionApp.Services;

public sealed class CovIngestionWorker(
    AppSettings settings,
    IngestionRuntimeOptions runtimeOptions,
    IngestionStatusTracker status,
    ILogger<CovIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        status.Configure(settings.Metasys.BaseUrl, runtimeOptions.SqlEnabled);

        try
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(settings.Metasys.BaseUrl, UriKind.Absolute),
                Timeout = Timeout.InfiniteTimeSpan
            };

            var requestedObjects = new[] { "WATER-001", "WATER-002", "TEMP-001" };
            logger.LogInformation("Connecting to Fake Metasys at {BaseUrl}", httpClient.BaseAddress);

            using var subscriptionResponse = await httpClient.PostAsJsonAsync(
                "api/metasys/subscriptions",
                new SubscriptionRequest { ObjectIds = [.. requestedObjects] },
                stoppingToken);
            subscriptionResponse.EnsureSuccessStatusCode();

            var subscription = await subscriptionResponse.Content.ReadFromJsonAsync<SubscriptionResponse>(
                cancellationToken: stoppingToken)
                ?? throw new InvalidOperationException("Fake Metasys returned an empty subscription response.");

            status.Connected(subscription.SubscriptionId);
            logger.LogInformation(
                "Created {SubscriptionId}; SQL persistence enabled: {SqlEnabled}",
                subscription.SubscriptionId,
                runtimeOptions.SqlEnabled);

            var repository = runtimeOptions.SqlEnabled
                ? new BmsReadingRepository(settings.Sql.ConnectionString)
                : null;

            using var streamResponse = await httpClient.GetAsync(
                $"api/metasys/subscriptions/{Uri.EscapeDataString(subscription.SubscriptionId)}/stream",
                HttpCompletionOption.ResponseHeadersRead,
                stoppingToken);
            streamResponse.EnsureSuccessStatusCode();

            await using var stream = await streamResponse.Content.ReadAsStreamAsync(stoppingToken);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(stoppingToken) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var covEvent = JsonSerializer.Deserialize<CovEvent>(line[5..].TrimStart(), AppSettings.JsonOptions);
                if (covEvent is null)
                {
                    continue;
                }

                status.EventReceived(covEvent);
                logger.LogInformation(
                    "COV {ObjectId}: {PreviousValue} -> {CurrentValue} {Unit}",
                    covEvent.ObjectId,
                    covEvent.PreviousValue,
                    covEvent.CurrentValue,
                    covEvent.Unit);

                if (repository is not null)
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
