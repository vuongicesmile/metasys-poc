using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;
using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.DataAccess.Hosting;

namespace DataverseSyncWorker.Hosting;

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
        services.AddSingleton<CommandProcessor>();
        services.AddSingleton<ICommandProcessor>(sp => sp.GetRequiredService<CommandProcessor>());
        services.AddSingleton<RuntimeState>();
        services.AddHostedService<SyncWorker>();
        services.AddTransient<WorkerCommandDispatcher>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
