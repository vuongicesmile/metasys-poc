using DataverseSyncWorker.Services;
using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.DataAccess.Abstractions;
using DataverseSyncWorker.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataverseSyncWorker.DataAccess.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSyncDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        // EF Core chỉ đọc catalog; delivery ledger vẫn dùng SqlStore/ADO.NET để giữ app lock và transaction.
        services.AddDbContextFactory<SqlSyncDbContext>(builder =>
            builder.UseSqlServer(configuration.GetConnectionString("Sql")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:Sql.")));
        services.AddSingleton<SqlStore>();
        // Một instance SqlStore phục vụ cả interface ledger và low-level store.
        services.AddSingleton<ISyncLedger>(sp => sp.GetRequiredService<SqlStore>());
        services.AddSingleton<ISqlStore>(sp => sp.GetRequiredService<SqlStore>());
        services.AddSingleton<ISqlCatalogReader, SqlCatalogReader>();
        services.AddSingleton<DataverseConnection>();
        services.AddSingleton<IIntegrationFailureClassifier, DataverseFailureClassifier>();
        services.AddSingleton<IDataverseWriter, DataverseWriter>();
        services.AddSingleton<ISyncBatchUnitOfWorkFactory, SqlSyncBatchUnitOfWorkFactory>();
        services.AddSingleton<SyncRequestStore>();
        services.AddSingleton<ISyncRequestStore>(services => services.GetRequiredService<SyncRequestStore>());
        services.AddTransient<DataverseProvisioner>();
        services.AddTransient<DataversePluginProvisioner>();
        services.AddTransient<IBmsRelationStore>(sp => new DataverseBmsRelationStore(sp.GetRequiredService<DataverseConnection>().Get()));
        services.AddTransient<BmsRelationshipSeeder>();
        return services;
    }
}
