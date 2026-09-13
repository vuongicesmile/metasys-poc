using BmsIngestionApp.Abstractions;
using BmsIngestionApp.Models;
using BmsIngestionApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BmsIngestionApp.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngestionServices(this IServiceCollection services, AppSettings settings, bool sqlEnabled = true)
    {
        services.AddSingleton(settings);
        services.AddSingleton(new IngestionRuntimeOptions(settings.Sql.Enabled && sqlEnabled));
        services.AddSingleton<IngestionStatusTracker>();
        services.AddSingleton<IBmsReadingRepository>(
            _ => new BmsReadingRepository(settings.Sql.ConnectionString));
        services.AddHttpClient(MetasysClient.ClientName, client =>
        {
            client.BaseAddress = new Uri(settings.Metasys.BaseUrl, UriKind.Absolute);
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        services.AddSingleton<IMetasysClient, MetasysClient>();
        services.AddHostedService<CovIngestionWorker>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
