using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.DataAccess.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BMS.Ingestion.DataAccess.Services;

/// <summary>EF Core Unit of Work dùng cùng một scoped DbContext cho mọi repository.</summary>
public sealed class BmsIngestionUnitOfWork(
    BmsIngestionDbContext db,
    IBmsCatalogRepository catalog,
    IBmsReadingRepository readings) : IBmsIngestionUnitOfWork
{
    public IBmsCatalogRepository Catalog { get; } = catalog;

    public IBmsReadingRepository Readings { get; } = readings;

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    // DbContext được DI scope sở hữu; scope sẽ trả context về pool khi dispose.
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Tạo và sở hữu DI scope cho từng Unit of Work của singleton BackgroundService.</summary>
public sealed class BmsIngestionUnitOfWorkFactory(IServiceScopeFactory scopeFactory)
    : IBmsIngestionUnitOfWorkFactory
{
    public async ValueTask<IBmsIngestionUnitOfWork> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = scopeFactory.CreateAsyncScope();

        try
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IBmsIngestionUnitOfWork>();
            return new ScopeOwnedUnitOfWork(unitOfWork, scope);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class ScopeOwnedUnitOfWork(
        IBmsIngestionUnitOfWork inner,
        AsyncServiceScope scope) : IBmsIngestionUnitOfWork
    {
        public IBmsCatalogRepository Catalog => inner.Catalog;

        public IBmsReadingRepository Readings => inner.Readings;

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
