using Microsoft.Extensions.DependencyInjection;
using Microsoft.PowerPlatform.Dataverse.Client;
using SPO.Ingestion.Business.Abstractions;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.DataAccess.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpoDataAccess(this IServiceCollection services,
        string storageConnectionString, string dataverseConnectionString)
    {
        services.AddSingleton(sp => new BlobJobStore(storageConnectionString,
            sp.GetRequiredService<SpoIngestionOptions>()));
        services.AddSingleton<ISpoJobStore>(sp => sp.GetRequiredService<BlobJobStore>());
        // DI sở hữu và dispose SDK client khi host dừng.
        services.AddSingleton(_ => new ServiceClient(dataverseConnectionString));
        services.AddSingleton<ISpoBronzeWriter, DataverseBronzeWriter>();
        return services;
    }
}
