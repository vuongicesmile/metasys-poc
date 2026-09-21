using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using SPO.Ingestion.Business.Abstractions;
using SPO.Ingestion.Business;
using SPO.Ingestion.DataAccess;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.App.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpoIngestionServices(
        this IServiceCollection services,
        SpoIngestionOptions options,
        string storageConnectionString,
        string dataverseConnectionString)
    {
        services.AddSingleton(options);
        services.AddSingleton(new BlobJobStore(storageConnectionString, options));
        services.AddSingleton<TabularParser>();
        services.AddSingleton<SpoBronzeMapper>();
        services.AddSingleton(new ServiceClient(dataverseConnectionString));
        services.AddSingleton<ISpoBronzeWriter, DataverseBronzeWriter>();
        services.AddSingleton<SpoJobProcessor>();
        return services;
    }
}
