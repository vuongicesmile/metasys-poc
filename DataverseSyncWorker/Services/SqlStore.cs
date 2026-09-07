using System.Data;
using System.Text.Json;
using DataverseSyncWorker.Models;
using Microsoft.Data.SqlClient;

namespace DataverseSyncWorker.Services;

public sealed class SqlStore(IConfiguration configuration, SyncOptions options)
{
    private string ConnectionString => configuration.GetConnectionString("Sql")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Sql.");
    public async Task<SqlConnection> Open(CancellationToken ct)
    {
        var c = new SqlConnection(ConnectionString);
        try { await c.OpenAsync(ct); return c; }
        catch { await c.DisposeAsync(); throw; }
    }

    public SqlCommand Command(SqlConnection c, string sql)
    {
        var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add("@pipeline", SqlDbType.VarChar, 150).Value = options.Pipeline;
        cmd.Parameters.Add("@history", SqlDbType.Bit).Value = options.HistoryEnabled;
        return cmd;
    }

    public async Task<bool> Lock(SqlConnection c, CancellationToken ct)
    {
        using var cmd = Command(c, "DECLARE @r int; EXEC @r=sp_getapplock @Resource=@pipeline, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @r;");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) >= 0;
    }
    public async Task Unlock(SqlConnection c)
    {
        using var cmd = Command(c, "EXEC sp_releaseapplock @Resource=@pipeline, @LockOwner='Session';");
        await cmd.ExecuteNonQueryAsync();
    }

    private const string Columns = "r.id,r.object_id,r.object_name,r.object_type,r.building,r.reading_time,r.reading_value,r.unit,r.source_system,r.ingested_at";
    private const string Pending = "(d.current_done IS NULL OR d.current_done=0 OR (@history=1 AND d.history_done=0))";
    public async Task<List<BmsReading>> ReadBatch(SqlConnection c, CancellationToken ct)
    {
        // Anti-join catches late commits with lower identity values. Never use NOLOCK/READPAST here.
        using var cmd = Command(c, $"SELECT TOP (@size) {Columns} FROM raw.bms_reading r LEFT JOIN integration.dataverse_delivery d ON d.pipeline=@pipeline AND d.reading_id=r.id WHERE {Pending} AND NOT EXISTS (SELECT 1 FROM integration.dataverse_dead_letter x WHERE x.pipeline=@pipeline AND x.bms_reading_id=r.id AND x.resolved_at IS NULL) ORDER BY r.id;");
        cmd.Parameters.Add("@size", SqlDbType.Int).Value = options.BatchSize;
        return await Read(cmd, ct);
    }

    public async Task<BmsReading> Latest(SqlConnection c, string objectId, CancellationToken ct)
    {
        using var cmd = Command(c, $"SELECT TOP(1) {Columns} FROM raw.bms_reading r WHERE r.object_id=@object ORDER BY r.reading_time DESC,r.id DESC;");
        cmd.Parameters.Add("@object", SqlDbType.VarChar, 100).Value = objectId;
        return (await Read(cmd, ct)).Single();
    }

    internal static async Task<List<BmsReading>> Read(SqlCommand cmd, CancellationToken ct)
    {
        var rows = new List<BmsReading>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
            rows.Add(new(r.GetInt64(0), r.GetString(1), S(2), S(3), S(4), r.GetDateTime(5),
                r.IsDBNull(6) ? null : r.GetDecimal(6), S(7), r.GetString(8), r.IsDBNull(9) ? null : r.GetDateTime(9)));
        }
        return rows;
    }

    public async Task Ack(SqlConnection c, BmsReading row, CancellationToken ct)
    {
        using var cmd = Command(c, """
            SET XACT_ABORT ON;
            BEGIN TRAN;
            UPDATE integration.dataverse_delivery SET current_done=1, history_done=CASE WHEN @history=1 THEN 1 ELSE history_done END, delivered_at=SYSUTCDATETIME() WHERE pipeline=@pipeline AND reading_id=@id;
            IF @@ROWCOUNT=0 INSERT integration.dataverse_delivery(pipeline,reading_id,current_done,history_done) VALUES(@pipeline,@id,1,@history);
            UPDATE integration.dataverse_sync_state SET last_successful_id=CASE WHEN @id>last_successful_id THEN @id ELSE last_successful_id END,last_completed_at=SYSUTCDATETIME(),updated_at=SYSUTCDATETIME() WHERE pipeline_name=@pipeline;
            IF @@ROWCOUNT=0 INSERT integration.dataverse_sync_state(pipeline_name,last_successful_id,last_completed_at) VALUES(@pipeline,@id,SYSUTCDATETIME());
            COMMIT;
            """);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = row.Id;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task Quarantine(SqlConnection c, BmsReading row, string reason, CancellationToken ct)
    {
        using var cmd = Command(c, """
            UPDATE integration.dataverse_dead_letter SET error_message=@error,attempt_count=attempt_count+1,last_failed_at=SYSUTCDATETIME(),resolved_at=NULL WHERE pipeline=@pipeline AND bms_reading_id=@id;
            IF @@ROWCOUNT=0 INSERT integration.dataverse_dead_letter(pipeline,bms_reading_id,target_table,payload_json,error_message) VALUES(@pipeline,@id,'validation',@payload,@error);
            """);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = row.Id;
        cmd.Parameters.Add("@error", SqlDbType.NVarChar, 2000).Value = reason;
        cmd.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(row);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SyncSummary> Summary(CancellationToken ct)
    {
        await using var c = await Open(ct);
        using var cmd = Command(c, $"""
            SELECT COUNT_BIG(*),
                COALESCE(SUM(CONVERT(bigint,CASE WHEN d.current_done=1 AND (@history=0 OR d.history_done=1) THEN 1 ELSE 0 END)),0),
                COALESCE(SUM(CONVERT(bigint,CASE WHEN {Pending} THEN 1 ELSE 0 END)),0),
                (SELECT COUNT_BIG(*) FROM integration.dataverse_dead_letter WHERE pipeline=@pipeline AND resolved_at IS NULL),
                (SELECT COALESCE(MAX(last_successful_id),0) FROM integration.dataverse_sync_state WHERE pipeline_name=@pipeline)
            FROM raw.bms_reading r LEFT JOIN integration.dataverse_delivery d ON d.pipeline=@pipeline AND d.reading_id=r.id;
            """);
        await using var r = await cmd.ExecuteReaderAsync(ct); await r.ReadAsync(ct);
        return new(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4));
    }

    public async Task<DeadLetter[]> DeadLetters(CancellationToken ct)
    {
        await using var c = await Open(ct);
        using var cmd = Command(c, "SELECT TOP(100) bms_reading_id,error_message,attempt_count,last_failed_at FROM integration.dataverse_dead_letter WHERE pipeline=@pipeline AND resolved_at IS NULL ORDER BY bms_reading_id;");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var result = new List<DeadLetter>();
        while (await r.ReadAsync(ct)) result.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt32(2),r.GetDateTime(3)));
        return result.ToArray();
    }
    public async Task<int> Replay(long id, CancellationToken ct)
    {
        await using var c = await Open(ct);
        using var cmd = Command(c, "UPDATE integration.dataverse_dead_letter SET resolved_at=SYSUTCDATETIME() WHERE pipeline=@pipeline AND bms_reading_id=@id AND resolved_at IS NULL;");
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
