using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BMS.Fake.DataAccess.Persistence;

/// <summary>Transaction ngắn hạn cho thao tác đọc và cập nhật current state.</summary>
public interface IFakeUnitOfWork : IAsyncDisposable
{
    FakeBmsDbContext Context { get; }
    Task CommitAsync(CancellationToken ct = default);
}

public interface IFakeUnitOfWorkFactory
{
    Task<IFakeUnitOfWork> CreateAsync(CancellationToken ct = default);
}

public sealed class FakeUnitOfWorkFactory(IDbContextFactory<FakeBmsDbContext> contexts)
    : IFakeUnitOfWorkFactory
{
    public async Task<IFakeUnitOfWork> CreateAsync(CancellationToken ct = default)
    {
        var db = await contexts.CreateDbContextAsync(ct);
        try
        {
            var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(ct) : null;
            return new UnitOfWork(db, transaction);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private sealed class UnitOfWork(FakeBmsDbContext db, IDbContextTransaction? transaction)
        : IFakeUnitOfWork
    {
        public FakeBmsDbContext Context => db;

        public async Task CommitAsync(CancellationToken ct = default)
        {
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (transaction is not null) await transaction.DisposeAsync();
            }
            finally { await db.DisposeAsync(); }
        }
    }
}
