using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Business.Services;
using BMS.Ingestion.Common.Configuration;
using BMS.Ingestion.DataAccess.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BMS.Ingestion.App.Hosting;

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
