using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;
using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.DataAccess.Abstractions;
using DataverseSyncWorker.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DataverseSyncWorker.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataverseSync(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind một object options duy nhất để các layer dùng cùng pipeline/retention/auth config.
        var options = configuration.GetSection("Dataverse").Get<SyncOptions>() ?? new();
        options.Validate();
        services.AddSingleton(options);
        // EF Core chỉ đọc catalog; delivery ledger vẫn dùng SqlStore/ADO.NET để giữ app lock và transaction.
        services.AddDbContextFactory<SqlSyncDbContext>(builder =>
            builder.UseSqlServer(configuration.GetConnectionString("Sql")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:Sql.")));
        services.AddSingleton<ReadingMapper>();
        services.AddSingleton<SqlStore>();
        // Một instance SqlStore phục vụ cả interface ledger và low-level store.
        services.AddSingleton<ISyncLedger>(sp => sp.GetRequiredService<SqlStore>());
        services.AddSingleton<ISqlStore>(sp => sp.GetRequiredService<SqlStore>());
        services.AddSingleton<ISqlCatalogReader, SqlCatalogReader>();
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
