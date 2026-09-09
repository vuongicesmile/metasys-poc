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
            equipment_code,
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
            @equipment_code,
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
        command.Parameters.Add("@equipment_code", SqlDbType.VarChar, 100).Value = covEvent.EquipmentCode;
        command.Parameters.Add("@reading_time", SqlDbType.DateTime2).Value = covEvent.Timestamp;

        var valueParameter = command.Parameters.Add("@reading_value", SqlDbType.Decimal);
        valueParameter.Precision = 18;
        valueParameter.Scale = 4;
        valueParameter.Value = covEvent.CurrentValue;

        command.Parameters.Add("@unit", SqlDbType.VarChar, 50).Value = covEvent.Unit;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PersistCatalogAsync(IReadOnlyList<BmsBuilding> buildings,
        IReadOnlyList<BmsEquipment> equipment, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Sql.ConnectionString must be configured when SQL is enabled.");
        var buildingCodes = buildings.Select(b => b.BuildingCode).ToHashSet(StringComparer.Ordinal);
        if (buildingCodes.Count != buildings.Count || equipment.Select(e => e.EquipmentCode).Distinct(StringComparer.Ordinal).Count() != equipment.Count)
            throw new InvalidOperationException("Fake Metasys catalog contains duplicate codes.");
        if (equipment.Any(e => !buildingCodes.Contains(e.BuildingCode)))
            throw new InvalidOperationException("Fake Metasys equipment refers to a missing building.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var building in buildings)
            {
                const string sql = """
                    UPDATE raw.bms_building SET name=@name,source_building=@source,description=@description,
                        source_updated_at=@updated,ingested_at=SYSUTCDATETIME() WHERE building_code=@code;
                    IF @@ROWCOUNT=0 INSERT raw.bms_building(building_code,name,source_building,description,source_updated_at)
                        VALUES(@code,@name,@source,@description,@updated);
                    """;
                await using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.Add("@code", SqlDbType.VarChar, 50).Value = building.BuildingCode;
                command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = building.Name;
                command.Parameters.Add("@source", SqlDbType.VarChar, 100).Value = building.SourceBuilding;
                command.Parameters.Add("@description", SqlDbType.NVarChar, 2000).Value = building.Description;
                command.Parameters.Add("@updated", SqlDbType.DateTime2).Value = DateTime.UtcNow;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var item in equipment)
            {
                const string sql = """
                    UPDATE raw.bms_equipment SET name=@name,equipment_type=@type,building_code=@building,
                        description=@description,source_updated_at=@updated,ingested_at=SYSUTCDATETIME() WHERE equipment_code=@code;
                    IF @@ROWCOUNT=0 INSERT raw.bms_equipment(equipment_code,name,equipment_type,building_code,description,source_updated_at)
                        VALUES(@code,@name,@type,@building,@description,@updated);
                    """;
                await using var command = new SqlCommand(sql, connection, transaction);
                command.Parameters.Add("@code", SqlDbType.VarChar, 100).Value = item.EquipmentCode;
                command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = item.Name;
                command.Parameters.Add("@type", SqlDbType.VarChar, 50).Value = item.EquipmentType;
                command.Parameters.Add("@building", SqlDbType.VarChar, 50).Value = item.BuildingCode;
                command.Parameters.Add("@description", SqlDbType.NVarChar, 2000).Value = item.Description;
                command.Parameters.Add("@updated", SqlDbType.DateTime2).Value = DateTime.UtcNow;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }
}
