using Dataverse.SyncWorker.DataAccess.Hosting;
using Dataverse.SyncWorker.Common.Configuration;
using Dataverse.SyncWorker.Business.Abstractions;
using Dataverse.SyncWorker.Business.Services;

namespace Dataverse.SyncWorker.App.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataverseSync(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind một object options duy nhất để các layer dùng cùng pipeline/retention/auth config.
        var options = configuration.GetSection("Dataverse").Get<SyncOptions>() ?? new();
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<ReadingMapper>();
        services.AddSyncDataAccess(configuration);
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<ISyncEngine>(sp => sp.GetRequiredService<SyncEngine>());
        services.AddSingleton<CommandProcessor>();
        services.AddSingleton<ICommandProcessor>(sp => sp.GetRequiredService<CommandProcessor>());
        services.AddSingleton<RuntimeState>();
        services.AddHostedService<Dataverse.SyncWorker.Business.Services.SyncWorker>();
        services.AddTransient<WorkerCommandDispatcher>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
