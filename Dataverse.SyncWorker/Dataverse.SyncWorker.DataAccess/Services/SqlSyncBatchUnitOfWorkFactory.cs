using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.DataAccess.Abstractions;
using DataverseSyncWorker.Models;
using Microsoft.Data.SqlClient;

namespace DataverseSyncWorker.Services;

public sealed class SqlSyncBatchUnitOfWorkFactory(ISqlStore store) : ISyncBatchUnitOfWorkFactory
{
    public async Task<ISyncBatchUnitOfWork?> TryCreate(CancellationToken ct)
    {
        var connection = await store.Open(ct);
        try
        {
            if (await store.Lock(connection, ct)) return new Session(store, connection);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
        await connection.DisposeAsync();
        return null;
    }

    private sealed class Session(ISqlStore store, SqlConnection connection) : ISyncBatchUnitOfWork
    {
        public Task<List<BmsReading>> ReadBatch(CancellationToken ct, long? cutoffId = null) => store.ReadBatch(connection, ct, cutoffId);
        public Task<BmsReading> Latest(string objectId, CancellationToken ct) => store.Latest(connection, objectId, ct);
        public Task Ack(BmsReading row, CancellationToken ct) => store.Ack(connection, row, ct);
        public Task Quarantine(BmsReading row, string reason, CancellationToken ct) => store.Quarantine(connection, row, reason, ct);
        public async ValueTask DisposeAsync()
        {
            try { await store.Unlock(connection); }
            finally { await connection.DisposeAsync(); }
        }
    }
}
