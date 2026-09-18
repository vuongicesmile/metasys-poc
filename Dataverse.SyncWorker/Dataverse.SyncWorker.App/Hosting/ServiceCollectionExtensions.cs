using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;
using DataverseSyncWorker.Abstractions;

namespace DataverseSyncWorker.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataverseSync(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Dataverse").Get<SyncOptions>() ?? new();
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<ReadingMapper>();
        services.AddSingleton<SqlStore>();
        services.AddSingleton<ISyncLedger>(sp => sp.GetRequiredService<SqlStore>());
        services.AddSingleton<DataverseConnection>();
        services.AddSingleton<IDataverseWriter, DataverseWriter>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<ISyncEngine>(sp => sp.GetRequiredService<SyncEngine>());
        services.AddSingleton<SyncRequestStore>();
        services.AddSingleton<ISyncRequestStore>(services => services.GetRequiredService<SyncRequestStore>());
        services.AddSingleton<CommandProcessor>();
        services.AddSingleton<ICommandProcessor>(sp => sp.GetRequiredService<CommandProcessor>());
        services.AddSingleton<RuntimeState>();
        services.AddHostedService<SyncWorker>();
        services.AddTransient<DataverseProvisioner>();
        services.AddTransient<DataversePluginProvisioner>();
        services.AddTransient<WorkerCommandDispatcher>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
