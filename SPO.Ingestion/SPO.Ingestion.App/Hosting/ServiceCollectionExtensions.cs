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
        services.AddSpoProcessing(options);
        services.AddSpoDataAccess(storageConnectionString, dataverseConnectionString);
        services.AddSingleton<SpoJobProcessor>();
        return services;
    }

    public static IServiceCollection AddSpoProcessing(this IServiceCollection services, SpoIngestionOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<TabularParser>();
        services.AddSingleton<SpoBronzeMapper>();
        services.AddSingleton<SpoRecordValidator>();
        services.AddSingleton<SpoPreviewer>();
        services.AddTransient<SpoLocalProcessor>();
        return services;
    }
}
