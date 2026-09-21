using Microsoft.Extensions.DependencyInjection;
using SPO.Ingestion.Business;
using SPO.Ingestion.DataAccess.Hosting;
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
        services.AddSpoDataAccess(storageConnectionString, dataverseConnectionString);
        services.AddSingleton<TabularParser>();
        services.AddSingleton<SpoBronzeMapper>();
        services.AddSingleton<SpoRecordValidator>();
        services.AddSingleton<SpoJobProcessor>();
        return services;
    }
}
