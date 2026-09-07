using System.Data;
using BmsIngestionApp.Models;
using Microsoft.Data.SqlClient;

namespace BmsIngestionApp.Services;

public sealed class BmsReadingRepository(string connectionString)
{
    private const string InsertSql = """
        INSERT INTO raw.bms_reading
        (
            object_id,
            object_name,
            object_type,
            building,
            reading_time,
            reading_value,
            unit,
            source_system
        )
        VALUES
        (
            @object_id,
            @object_name,
            @object_type,
            @building,
            @reading_time,
            @reading_value,
            @unit,
            'Fake Metasys COV'
        );
        """;

    public async Task InsertAsync(CovEvent covEvent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Sql.ConnectionString must be configured when SQL is enabled.");
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(InsertSql, connection);
        command.Parameters.Add("@object_id", SqlDbType.VarChar, 100).Value = covEvent.ObjectId;
        command.Parameters.Add("@object_name", SqlDbType.VarChar, 200).Value = covEvent.ObjectName;
        command.Parameters.Add("@object_type", SqlDbType.VarChar, 100).Value = covEvent.ObjectType;
        command.Parameters.Add("@building", SqlDbType.VarChar, 100).Value = covEvent.Building;
        command.Parameters.Add("@reading_time", SqlDbType.DateTime2).Value = covEvent.Timestamp;

        var valueParameter = command.Parameters.Add("@reading_value", SqlDbType.Decimal);
        valueParameter.Precision = 18;
        valueParameter.Scale = 4;
        valueParameter.Value = covEvent.CurrentValue;

        command.Parameters.Add("@unit", SqlDbType.VarChar, 50).Value = covEvent.Unit;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
