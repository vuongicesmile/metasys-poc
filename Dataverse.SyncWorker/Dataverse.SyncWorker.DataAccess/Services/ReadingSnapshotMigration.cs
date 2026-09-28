using Dataverse.SyncWorker.Business.Services;
using Dataverse.SyncWorker.Common.Configuration;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.SyncWorker.DataAccess.Services;

/// <summary>One-time 10 September 2026 (UTC+7) demo snapshot, independent of delivery receipts.</summary>
public sealed class ReadingSnapshotMigration(DataverseConnection connection, SqlStore sql,
    ReadingMapper mapper)
{
    public const string Table = "fmc_bmsreadingsnapshot";
    public const int ExpectedRows = 7863;
    private static readonly DateTime StartUtc = new(2026, 9, 9, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndUtc = new(2026, 9, 10, 17, 0, 0, DateTimeKind.Utc);

    public async Task Prepare(CancellationToken ct)
    {
        var client = connection.Get();
        var solution = new QueryExpression("solution") { ColumnSet = new ColumnSet("solutionid"), TopCount = 2 };
        solution.Criteria.AddCondition("uniquename", ConditionOperator.Equal, DataverseProvisioner.Solution);
        if ((await client.RetrieveMultipleAsync(solution)).Entities.Count != 1)
            throw new InvalidOperationException("Expected exactly one FMCentralBms solution.");
        var metadata = await Metadata(client);
        if (metadata is null)
        {
            await client.ExecuteAsync(new CreateEntityRequest
            {
                SolutionUniqueName = DataverseProvisioner.Solution,
                Entity = new EntityMetadata
                {
                    SchemaName = Table,
                    DisplayName = new Label("BMS Reading Snapshot", 1033),
                    DisplayCollectionName = new Label("BMS Reading Snapshots", 1033),
                    Description = new Label("Fixed demo snapshot for 10 September 2026, Vietnam time; raw history remains in SQL.", 1033),
                    OwnershipType = OwnershipTypes.OrganizationOwned,
                    TableType = "Standard",
                    IsActivity = false,
                    IsAuditEnabled = new BooleanManagedProperty(false)
                },
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = "fmc_name", DisplayName = new Label("Name", 1033), MaxLength = 200,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
                    FormatName = StringFormatName.Text
                }
            });
            metadata = await Metadata(client);
        }
        if (metadata?.TableType != "Standard")
            throw new InvalidOperationException($"{Table} must be a Standard table.");
        await client.ExecuteAsync(new AddSolutionComponentRequest
        {
            ComponentId = metadata.MetadataId!.Value, ComponentType = 1,
            SolutionUniqueName = DataverseProvisioner.Solution, AddRequiredComponents = false
        });
        foreach (var column in DataverseProvisioner.Columns(true))
        {
            var present = metadata.Attributes.SingleOrDefault(a => a.LogicalName == column.SchemaName);
            if (present is null)
                await client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = Table, Attribute = column,
                    SolutionUniqueName = DataverseProvisioner.Solution
                });
            else if (present.GetType() != column.GetType())
                throw new InvalidOperationException($"{Table}.{column.SchemaName} type mismatch.");
        }
        await client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{Table}</entity></entities></importexportxml>"
        });
        metadata = await Metadata(client) ?? throw new InvalidOperationException("Snapshot table not published.");
        if (!metadata.Attributes.Any(a => a.LogicalName == "fmc_equipmentcode"))
            throw new InvalidOperationException("Snapshot Equipment Code is not published.");
        await EnsureView(client, metadata.ObjectTypeCode!.Value);
        await EnsureReadRoles(client);
        await client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{Table}</entity></entities></importexportxml>"
        });
        Console.WriteLine($"Prepared Standard table {Table} with Equipment Code and demo view.");
    }

    public async Task Import(CancellationToken ct)
    {
        var client = connection.Get();
        var metadata = await Metadata(client);
        if (metadata?.TableType != "Standard" || !metadata.Attributes.Any(a => a.LogicalName == "fmc_equipmentcode"))
            throw new InvalidOperationException("Snapshot schema is not ready; run --prepare-reading-snapshot first.");
        await using var db = await sql.Open(ct);
        using var command = sql.Command(db, """
            SELECT id,object_id,object_name,object_type,building,equipment_code,reading_time,reading_value,unit,source_system,ingested_at
            FROM raw.bms_reading
            WHERE reading_time >= @start AND reading_time < @end ORDER BY id;
            """);
        command.Parameters.Add("@start", System.Data.SqlDbType.DateTime2).Value = StartUtc;
        command.Parameters.Add("@end", System.Data.SqlDbType.DateTime2).Value = EndUtc;
        var source = await SqlStore.Read(command, ct);
        if (source.Count != ExpectedRows || source.Any(r => string.IsNullOrWhiteSpace(r.EquipmentCode)))
            throw new InvalidOperationException($"Expected {ExpectedRows} SQL rows with Equipment Code; found {source.Count}. No records imported.");
        var existing = new HashSet<Guid>();
        var query = new QueryExpression(Table)
        {
            ColumnSet = new ColumnSet(false), PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        while (true)
        {
            var page = await client.RetrieveMultipleAsync(query);
            foreach (var row in page.Entities) existing.Add(row.Id);
            if (!page.MoreRecords) break;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
        if (existing.Except(source.Select(r => mapper.ReadingId(r.Id))).Any())
            throw new InvalidOperationException("Snapshot table contains unrelated rows; refusing to mix demo dates.");
        var missing = source.Where(r => !existing.Contains(mapper.ReadingId(r.Id))).ToArray();
        var remaining = missing.Length;
        foreach (var chunk in missing.Chunk(100))
        {
            var rows = chunk.Select(r =>
            {
                if (mapper.Validate(r) is { } error) throw new InvalidOperationException($"SQL row {r.Id}: {error}");
                var mapped = mapper.Snapshot(r);
                var entity = new Entity(Table, mapped.Id);
                foreach (var (name, value) in mapped.Attributes) entity[name] = value;
                return entity;
            }).ToList();
            await client.ExecuteAsync(new CreateMultipleRequest
            {
                Targets = new EntityCollection(rows) { EntityName = Table }
            });
            remaining -= rows.Count;
            Console.WriteLine($"Imported {rows.Count} readings; remaining {remaining}.");
        }
        await Verify(ct);
    }

    public async Task Verify(CancellationToken ct)
    {
        var client = connection.Get();
        var query = new QueryExpression(Table)
        {
            ColumnSet = new ColumnSet("fmc_sqlreadingid", "fmc_equipmentcode", "fmc_readingtime"),
            PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
        };
        var count = 0;
        var blankCode = 0;
        var outsideDay = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await client.RetrieveMultipleAsync(query);
            foreach (var row in page.Entities)
            {
                count++;
                if (string.IsNullOrWhiteSpace(row.GetAttributeValue<string>("fmc_equipmentcode"))) blankCode++;
                var time = row.GetAttributeValue<DateTime>("fmc_readingtime");
                if (time < StartUtc || time >= EndUtc) outsideDay++;
            }
            if (!page.MoreRecords) break;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
        Console.WriteLine($"Snapshot verification: rows={count}, blank Equipment Code={blankCode}, outside 10/09 Vietnam={outsideDay}.");
        if (count != ExpectedRows || blankCode != 0 || outsideDay != 0)
            throw new InvalidOperationException("Snapshot verification failed; elastic history must not be removed.");
    }

    private static async Task<EntityMetadata?> Metadata(ServiceClient client)
    {
        try
        {
            return ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
            {
                LogicalName = Table, EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                RetrieveAsIfPublished = true
            })).EntityMetadata;
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
            when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                  ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        { return null; }
    }

    private static async Task EnsureView(ServiceClient client, int objectTypeCode)
    {
        const string name = "BMS Readings - 10 Sep 2026";
        var query = new QueryExpression("savedquery") { ColumnSet = new ColumnSet("savedqueryid"), TopCount = 2 };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
        query.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, objectTypeCode);
        query.Criteria.AddCondition("querytype", ConditionOperator.Equal, 0);
        var matches = (await client.RetrieveMultipleAsync(query)).Entities;
        if (matches.Count > 1) throw new InvalidOperationException("Duplicate snapshot views.");
        string[] columns = ["fmc_objectid", "fmc_objectname", "fmc_equipmentcode", "fmc_building",
            "fmc_readingvalue", "fmc_unit", "fmc_readingtime", "fmc_sqlreadingid", "fmc_sourcesystem"];
        var fetch = string.Join("", new[] { Table + "id" }.Concat(columns).Select(c => $"<attribute name=\"{c}\" />"));
        var cells = string.Join("", columns.Select(c => $"<cell name=\"{c}\" width=\"150\" />"));
        var values = new Entity("savedquery")
        {
            ["name"] = name, ["returnedtypecode"] = objectTypeCode, ["querytype"] = 0,
            ["fetchxml"] = $"<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"{Table}\">{fetch}<order attribute=\"fmc_readingtime\" descending=\"true\" /></entity></fetch>",
            ["layoutxml"] = $"<grid name=\"resultset\" object=\"{objectTypeCode}\" jump=\"fmc_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"{Table}id\">{cells}</row></grid>"
        };
        var id = matches.Count == 0 ? await client.CreateAsync(values) : matches[0].Id;
        if (matches.Count == 1)
            await client.UpdateAsync(new Entity("savedquery", id)
            {
                ["fetchxml"] = values["fetchxml"], ["layoutxml"] = values["layoutxml"]
            });
        await client.ExecuteAsync(new AddSolutionComponentRequest
        {
            ComponentId = id, ComponentType = 26, SolutionUniqueName = DataverseProvisioner.Solution,
            AddRequiredComponents = false
        });
    }

    private static async Task EnsureReadRoles(ServiceClient client)
    {
        var metadata = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = Table, EntityFilters = EntityFilters.Privileges
        })).EntityMetadata;
        var read = metadata.Privileges.Single(p => p.PrivilegeType == PrivilegeType.Read);
        foreach (var name in new[] { "FMC BMS Demo Viewer", "FMC BMS Demo Operator" })
        {
            var query = new QueryExpression("role") { ColumnSet = new ColumnSet("roleid"), TopCount = 2 };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
            query.Criteria.AddCondition("parentroleid", ConditionOperator.Null);
            var matches = (await client.RetrieveMultipleAsync(query)).Entities;
            if (matches.Count != 1) throw new InvalidOperationException($"Expected one root role {name}.");
            await client.ExecuteAsync(new AddPrivilegesRoleRequest
            {
                RoleId = matches[0].Id,
                Privileges = [new RolePrivilege { PrivilegeId = read.PrivilegeId, Depth = PrivilegeDepth.Global }]
            });
        }
    }
}
