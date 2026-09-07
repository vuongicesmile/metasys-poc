using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

/// <summary>Explicit deployment command; never runs as part of ordinary synchronization.</summary>
public sealed class DataverseProvisioner(DataverseConnection connection)
{
    public const string Solution = "FMCentralBms";
    public async Task Run()
    {
        var client = connection.Get();
        var publishers = await client.RetrieveMultipleAsync(Query("publisher", "uniquename", "FMCentralBmsPublisher"));
        var publisherId = publishers.Entities.FirstOrDefault()?.Id ?? await client.CreateAsync(new Entity("publisher")
        {
            ["uniquename"] = "FMCentralBmsPublisher", ["friendlyname"] = "FM Central BMS Publisher",
            ["customizationprefix"] = "fmc", ["customizationoptionvalueprefix"] = 78910
        });
        var solutions = await client.RetrieveMultipleAsync(Query("solution", "uniquename", Solution));
        if (solutions.Entities.Count == 0)
            await client.CreateAsync(new Entity("solution")
            {
                ["uniquename"] = Solution, ["friendlyname"] = "FM Central BMS",
                ["version"] = "1.0.0.0", ["publisherid"] = new EntityReference("publisher", publisherId)
            });

        var all = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity });
        foreach (var (table, label, elastic) in new[] { ("fmc_bmspoint", "BMS Point", false), ("fmc_bmsreading", "BMS Reading", true) })
        {
            var existing = all.EntityMetadata.SingleOrDefault(e => e.LogicalName == table);
            if (existing is not null && (existing.TableType == "Elastic") != elastic)
                throw new InvalidOperationException($"{table} exists with a different table type; refusing to alter it.");
            if (existing is null)
            {
                await client.ExecuteAsync(new CreateEntityRequest
                {
                    SolutionUniqueName = Solution,
                    Entity = new EntityMetadata
                    {
                        SchemaName = table, DisplayName = new Label(label, 1033),
                        DisplayCollectionName = new Label(label + "s", 1033),
                        OwnershipType = elastic ? OwnershipTypes.UserOwned : OwnershipTypes.OrganizationOwned,
                        TableType = elastic ? "Elastic" : "Standard",
                        IsActivity = false, IsAuditEnabled = new BooleanManagedProperty(false),
                        CanCreateCharts = new BooleanManagedProperty(!elastic)
                    },
                    PrimaryAttribute = Text("fmc_name", "Name", 200)
                });
            }

            var metadata = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
            {
                LogicalName = table, EntityFilters = EntityFilters.Entity | EntityFilters.Attributes,
                RetrieveAsIfPublished = true
            })).EntityMetadata;
            await client.ExecuteAsync(new AddSolutionComponentRequest
            {
                ComponentId = metadata.MetadataId!.Value, ComponentType = 1,
                SolutionUniqueName = Solution, AddRequiredComponents = false
            });
            foreach (var attr in Columns(elastic))
            {
                var present = metadata.Attributes.SingleOrDefault(a => a.LogicalName == attr.SchemaName);
                if (present is null)
                    await client.ExecuteAsync(new CreateAttributeRequest { EntityName = table, Attribute = attr, SolutionUniqueName = Solution });
                else if (present.GetType() != attr.GetType())
                    throw new InvalidOperationException($"{table}.{attr.SchemaName} type mismatch.");
            }
            Console.WriteLine($"Provisioned {table} ({(elastic ? "elastic" : "standard")}).");
        }

        var pointMeta = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = "fmc_bmspoint", EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
        })).EntityMetadata;
        if (!(pointMeta.Keys ?? []).Any(k => k.LogicalName == "fmc_bmspoint_objectid"))
            await client.ExecuteAsync(new CreateEntityKeyRequest
            {
                EntityName = "fmc_bmspoint", SolutionUniqueName = Solution,
                EntityKey = new EntityKeyMetadata
                {
                    SchemaName = "fmc_bmspoint_objectid", DisplayName = new Label("BMS Object ID", 1033),
                    KeyAttributes = ["fmc_objectid"]
                }
            });
        // Elastic uses its built-in primary GUID + partitionid key only.
        await ConfigureDefaultView(client, "fmc_bmspoint", "Active BMS Points",
            ["fmc_objectid", "fmc_name", "fmc_objecttype", "fmc_currentvalue", "fmc_unit", "fmc_lastreadingtime", "fmc_building"]);
        await ConfigureDefaultView(client, "fmc_bmsreading", "All BMS Readings",
            ["fmc_objectid", "fmc_objectname", "fmc_objecttype", "fmc_readingvalue", "fmc_unit", "fmc_readingtime", "fmc_building"]);
        await client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><entities><entity>fmc_bmspoint</entity><entity>fmc_bmsreading</entity></entities></importexportxml>"
        });

        var rootQuery = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid") };
        rootQuery.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
        var root = (await client.RetrieveMultipleAsync(rootQuery)).Entities.Single().Id;
        var roles = Query("role", "name", "FM Central BMS Integration");
        roles.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, root);
        var existingRoles = await client.RetrieveMultipleAsync(roles);
        var roleId = existingRoles.Entities.FirstOrDefault()?.Id ?? await client.CreateAsync(new Entity("role")
        {
            ["name"] = "FM Central BMS Integration", ["businessunitid"] = new EntityReference("businessunit", root)
        });
        var privileges = new List<RolePrivilege>();
        foreach (var table in new[] { "fmc_bmspoint", "fmc_bmsreading" })
        {
            var meta = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
                { LogicalName = table, EntityFilters = EntityFilters.Privileges })).EntityMetadata;
            privileges.AddRange(meta.Privileges
                .Where(p => p.PrivilegeType is PrivilegeType.Create or PrivilegeType.Read or PrivilegeType.Write)
                .Select(p => new RolePrivilege { PrivilegeId = p.PrivilegeId, Depth = PrivilegeDepth.Global }));
        }
        await client.ExecuteAsync(new AddPrivilegesRoleRequest { RoleId = roleId, Privileges = privileges.ToArray() });
        await client.ExecuteAsync(new AddSolutionComponentRequest
            { ComponentId = roleId, ComponentType = 20, SolutionUniqueName = Solution, AddRequiredComponents = false });
        Console.WriteLine($"Runtime role ready: {roleId}. Assign this role to the runtime application user; remove deployment roles before continuous sync.");
        Console.WriteLine("Provisioning complete. Export with pac solution export --name FMCentralBms --path dataverse/FMCentralBms.zip");
    }

    private static async Task ConfigureDefaultView(ServiceClient client, string table,
        string viewName, string[] columns)
    {
        var query = new QueryExpression("savedquery")
        {
            ColumnSet = new ColumnSet("savedqueryid", "name"),
            TopCount = 2
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, viewName);
        query.Criteria.AddCondition("querytype", ConditionOperator.Equal, 0);
        var matches = await client.RetrieveMultipleAsync(query);
        if (matches.Entities.Count != 1)
            throw new InvalidOperationException($"Expected exactly one public view named '{viewName}'.");

        var tableMetadata = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = table, EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata;
        var objectTypeCode = tableMetadata.ObjectTypeCode ??
            throw new InvalidOperationException($"Dataverse did not return ObjectTypeCode for {table}.");
        var primaryId = table + "id";
        var fetchColumns = string.Join("", new[] { primaryId }.Concat(columns)
            .Select(name => $"<attribute name=\"{name}\" />"));
        var cells = string.Join("", columns.Select((name, index) =>
            $"<cell name=\"{name}\" width=\"{(index is 0 or 1 ? 220 : 140)}\" />"));
        await client.UpdateAsync(new Entity("savedquery", matches.Entities.Single().Id)
        {
            ["fetchxml"] = $"<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"{table}\">{fetchColumns}<order attribute=\"{(table == "fmc_bmspoint" ? "fmc_name" : "fmc_readingtime")}\" descending=\"{(table == "fmc_bmspoint" ? "false" : "true")}\" /></entity></fetch>",
            ["layoutxml"] = $"<grid name=\"resultset\" object=\"{objectTypeCode}\" jump=\"fmc_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"{primaryId}\">{cells}</row></grid>"
        });
        Console.WriteLine($"Configured default view '{viewName}' with {columns.Length} business columns.");
    }

    private static QueryExpression Query(string table, string name, string value)
    {
        var q = new QueryExpression(table) { ColumnSet = new ColumnSet(false) };
        q.Criteria.AddCondition(name, ConditionOperator.Equal, value); return q;
    }
    private static StringAttributeMetadata Text(string name, string label, int length) => new()
    {
        SchemaName = name, DisplayName = new Label(label, 1033), MaxLength = length,
        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None), FormatName = StringFormatName.Text
    };
    private static DateTimeAttributeMetadata Time(string name, string label) => new()
    {
        SchemaName = name, DisplayName = new Label(label, 1033), Format = DateTimeFormat.DateAndTime,
        DateTimeBehavior = DateTimeBehavior.UserLocal, ImeMode = ImeMode.Disabled
    };
    public static IEnumerable<AttributeMetadata> Columns(bool history)
    {
        yield return Text("fmc_objectid", "Object ID", 100);
        yield return Text("fmc_objecttype", "Object Type", 100);
        yield return Text("fmc_building", "Building", 100);
        yield return Text("fmc_unit", "Unit", 50);
        yield return Text("fmc_sourcesystem", "Source System", 100);
        yield return Text(history ? "fmc_sqlreadingid" : "fmc_lastsqlid", "SQL Reading ID", 20);
        yield return new DecimalAttributeMetadata
        {
            SchemaName = history ? "fmc_readingvalue" : "fmc_currentvalue",
            DisplayName = new Label(history ? "Reading Value" : "Current Value", 1033),
            Precision = 4, MinValue = -100000000000m, MaxValue = 100000000000m
        };
        yield return Time(history ? "fmc_readingtime" : "fmc_lastreadingtime", "Reading Time");
        if (history)
        {
            yield return Text("fmc_externalkey", "External Key (reference)", 100);
            yield return Text("fmc_objectname", "Object Name", 200);
            yield return Time("fmc_sqlingestedat", "SQL Ingested At");
        }
    }
}
