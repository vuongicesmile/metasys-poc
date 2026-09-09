using System.Text.Json;
using System.Xml.Linq;
using DataverseSyncWorker.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

// Additive migration: explicitly invoked, independent of flow/role provisioning and SQL sync.
public sealed partial class DataverseProvisioner
{
    public const string BuildingTable = "fmc_bmsbuilding";
    public const string EquipmentTable = "fmc_bmsequipment";
    public const string PointTable = "fmc_bmspoint";
    public const string BuildingRelationship = "fmc_bmsbuilding_bmsequipment";
    public const string EquipmentRelationship = "fmc_bmsequipment_bmspoint";
    internal static readonly string[] RelationTables = [BuildingTable, EquipmentTable, PointTable];

    private static StringAttributeMetadata RelationText(string name, string label, int length) => new()
    {
        SchemaName = name, DisplayName = new Label(label, 1033), MaxLength = length,
        RequiredLevel = new(AttributeRequiredLevel.ApplicationRequired), FormatName = StringFormatName.Text
    };

    internal static IEnumerable<AttributeMetadata> RelationColumns(string table)
    {
        yield return RelationText("fmc_name", "Name", 200);
        yield return RelationText(table == BuildingTable ? "fmc_buildingcode" : "fmc_equipmentcode", "Code", table == BuildingTable ? 50 : 100);
        if (table == BuildingTable)
            yield return RelationText("fmc_sourcebuilding", "Source Building", 100);
        else
            yield return new PicklistAttributeMetadata
            {
                SchemaName = "fmc_equipmenttype", DisplayName = new Label("Equipment Type", 1033),
                RequiredLevel = new(AttributeRequiredLevel.ApplicationRequired),
                OptionSet = new OptionSetMetadata
                {
                    IsGlobal = false,
                    Options =
                    {
                        new(new Label("Water Meter", 1033), BmsRelationManifest.TypeValue("WaterMeter")),
                        new(new Label("Temperature Sensor", 1033), BmsRelationManifest.TypeValue("TemperatureSensor")),
                        new(new Label("Test Rig", 1033), BmsRelationManifest.TypeValue("TestRig"))
                    }
                }
            };
        yield return new MemoAttributeMetadata
        {
            SchemaName = "fmc_description", DisplayName = new Label("Description", 1033),
            MaxLength = 2000, RequiredLevel = new(AttributeRequiredLevel.None)
        };
    }

    public async Task<Dictionary<string, EntityMetadata>> ReadBmsRelationMetadata(bool requireReady)
    {
        var client = connection.Get(); // WhoAmI organization guard is enforced by this connection.
        var solutionQuery = Query("solution", "uniquename", Solution);
        solutionQuery.ColumnSet = new ColumnSet("publisherid");
        var solutions = (await client.RetrieveMultipleAsync(solutionQuery)).Entities;
        if (solutions.Count != 1) throw new InvalidOperationException($"Expected existing solution {Solution}.");
        var publisher = await client.RetrieveAsync("publisher", solutions[0].GetAttributeValue<EntityReference>("publisherid").Id,
            new ColumnSet("uniquename", "customizationprefix"));
        if (publisher.GetAttributeValue<string>("uniquename") != "FMCentralBmsPublisher" ||
            publisher.GetAttributeValue<string>("customizationprefix") != "fmc")
            throw new InvalidOperationException("Solution publisher differs from the BMS contract.");
        var all = ((RetrieveAllEntitiesResponse)await client.ExecuteAsync(new RetrieveAllEntitiesRequest
            { EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true })).EntityMetadata;
        var result = new Dictionary<string, EntityMetadata>();
        foreach (var table in RelationTables.Append("fmc_bmsreading"))
        {
            if (!all.Any(e => e.LogicalName == table))
            {
                if (requireReady || table is PointTable or "fmc_bmsreading")
                    throw new InvalidOperationException($"Missing required table: {table}.");
                continue;
            }
            var metadata = await RelationMetadata(table);
            if ((metadata.TableType == "Elastic") != (table == "fmc_bmsreading"))
                throw new InvalidOperationException($"Unexpected table type: {table} ({metadata.TableType}).");
            if (table != "fmc_bmsreading" && metadata.OwnershipType != OwnershipTypes.OrganizationOwned)
                throw new InvalidOperationException($"Unexpected ownership: {table}.");
            result.Add(table, metadata);
        }
        if (result[PointTable].IsOptimisticConcurrencyEnabled != true)
            throw new InvalidOperationException("BMS Point must support optimistic concurrency before lookup updates.");
        var pointKey = result[PointTable].Keys?.SingleOrDefault(k => k.LogicalName == "fmc_bmspoint_objectid");
        if (pointKey?.EntityKeyIndexStatus != EntityKeyIndexStatus.Active ||
            !pointKey.KeyAttributes.SequenceEqual(["fmc_objectid"]))
            throw new InvalidOperationException("The existing BMS Object ID alternate key must be Active and unchanged.");
        foreach (var table in new[] { BuildingTable, EquipmentTable })
        {
            if (!result.TryGetValue(table, out var meta)) continue;
            foreach (var desired in RelationColumns(table))
            {
                var actual = meta.Attributes.SingleOrDefault(a => a.LogicalName == desired.SchemaName);
                if (actual is null)
                {
                    if (requireReady) throw new InvalidOperationException($"Missing {table}.{desired.SchemaName}.");
                }
                else ValidateRelationColumn(table, actual, desired);
            }
            var keyColumn = table == BuildingTable ? "fmc_buildingcode" : "fmc_equipmentcode";
            var keyName = table + (table == BuildingTable ? "_buildingcode" : "_equipmentcode");
            var key = meta.Keys?.SingleOrDefault(k => k.LogicalName == keyName);
            if (key is not null && !key.KeyAttributes.SequenceEqual([keyColumn]))
                throw new InvalidOperationException($"Alternate key mismatch: {keyName}.");
            if (requireReady && key?.EntityKeyIndexStatus != EntityKeyIndexStatus.Active)
                throw new InvalidOperationException($"Alternate key is not Active: {keyName}.");
        }
        ValidateRelationDefinition(result, BuildingTable, EquipmentTable, "fmc_buildingid", BuildingRelationship, true, requireReady);
        ValidateRelationDefinition(result, EquipmentTable, PointTable, "fmc_equipmentid", EquipmentRelationship, false, requireReady);
        if (requireReady)
        {
            foreach (var table in RelationTables)
                await RequireSolutionComponent(result[table].MetadataId!.Value, 1);
        }
        return result;
    }

    internal static void ValidateRelationColumn(string table, AttributeMetadata actual, AttributeMetadata desired)
    {
        var mismatch = actual.GetType() != desired.GetType() || actual.RequiredLevel?.Value != desired.RequiredLevel?.Value;
        if (actual is StringAttributeMetadata a && desired is StringAttributeMetadata b) mismatch |= a.MaxLength != b.MaxLength;
        if (actual is MemoAttributeMetadata m && desired is MemoAttributeMetadata n) mismatch |= m.MaxLength != n.MaxLength;
        if (actual is PicklistAttributeMetadata p && desired is PicklistAttributeMetadata q)
            mismatch |= p.OptionSet.IsGlobal != false || !p.OptionSet.Options.Select(o => o.Value).Order()
                .SequenceEqual(q.OptionSet.Options.Select(o => o.Value).Order());
        if (mismatch) throw new InvalidOperationException($"Existing metadata differs: {table}.{desired.SchemaName}. Review a migration; no destructive repair is attempted.");
    }

    private static void ValidateRelationDefinition(Dictionary<string, EntityMetadata> tables, string parent,
        string child, string lookup, string relationship, bool required, bool strict)
    {
        if (!tables.TryGetValue(child, out var meta)) return;
        var attribute = meta.Attributes.SingleOrDefault(a => a.LogicalName == lookup);
        var relation = meta.ManyToOneRelationships.SingleOrDefault(r => r.SchemaName == relationship);
        if (attribute is null && relation is null && !strict) return;
        if (attribute is not LookupAttributeMetadata l || !l.Targets.SequenceEqual([parent]) ||
            l.RequiredLevel.Value != (required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None) ||
            relation is null || relation.ReferencedEntity != parent || relation.ReferencingEntity != child ||
            relation.ReferencingAttribute != lookup || relation.ReferencedAttribute != parent + "id" ||
            relation.CascadeConfiguration.Delete != CascadeType.Restrict ||
            relation.CascadeConfiguration.Assign != CascadeType.NoCascade ||
            relation.CascadeConfiguration.Share != CascadeType.NoCascade ||
            relation.CascadeConfiguration.Unshare != CascadeType.NoCascade ||
            relation.CascadeConfiguration.Reparent != CascadeType.NoCascade ||
            relation.CascadeConfiguration.Merge != CascadeType.NoCascade)
            throw new InvalidOperationException($"Missing or conflicting relationship: {relationship}.");
    }

    public async Task PrintBmsRelationStatus(BmsRelationManifest manifest)
    {
        var metadata = await ReadBmsRelationMetadata(false);
        var who = (WhoAmIResponse)await connection.Get().ExecuteAsync(new WhoAmIRequest());
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            observedAtUtc = DateTime.UtcNow, organizationId = who.OrganizationId, userId = who.UserId,
            url = options.Url, solution = Solution, manifest.Version,
            tables = RelationTables.Select(t => new
            {
                name = t, exists = metadata.ContainsKey(t),
                type = metadata.GetValueOrDefault(t)?.TableType,
                entitySet = metadata.GetValueOrDefault(t)?.EntitySetName,
                keys = metadata.GetValueOrDefault(t)?.Keys?.Select(k => new { k.LogicalName, status = k.EntityKeyIndexStatus.ToString() }),
                relationships = metadata.GetValueOrDefault(t)?.ManyToOneRelationships?
                    .Where(r => r.SchemaName is BuildingRelationship or EquipmentRelationship)
                    .Select(r => new { r.SchemaName, r.ReferencingAttribute, r.ReferencedEntity, r.ReferencingEntityNavigationPropertyName })
            })
        }, BmsRelationManifest.Json));
        var lookupPresent = metadata[PointTable].Attributes.Any(a => a.LogicalName == "fmc_equipmentid");
        var store = new DataverseBmsRelationStore(connection.Get());
        foreach (var mapping in manifest.PointMappings)
        {
            var point = await store.Find(PointTable, "fmc_objectid", mapping.ObjectId,
                lookupPresent ? BmsRelationshipSeeder.PointColumns : BmsRelationshipSeeder.PointColumns.Where(c => c != "fmc_equipmentid").ToArray());
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                mapping.ObjectId, exists = point is not null, id = point?.Id,
                expectedId = new ReadingMapper(options).PointId(mapping.ObjectId),
                sourceBuilding = point?.GetAttributeValue<string>("fmc_building"),
                equipmentId = point?.GetAttributeValue<EntityReference>("fmc_equipmentid")?.Id
            }, BmsRelationManifest.Json));
        }
    }

    public async Task ProvisionBmsRelations()
    {
        var before = await ReadBmsRelationMetadata(false);
        var client = connection.Get();
        foreach (var (table, label) in new[] { (BuildingTable, "BMS Building"), (EquipmentTable, "BMS Equipment") })
        {
            if (!before.ContainsKey(table))
                await client.ExecuteAsync(new CreateEntityRequest
                {
                    SolutionUniqueName = Solution,
                    Entity = new EntityMetadata
                    {
                        SchemaName = table, DisplayName = new Label(label, 1033),
                        DisplayCollectionName = new Label(label == "BMS Equipment" ? label : label + "s", 1033),
                        OwnershipType = OwnershipTypes.OrganizationOwned, TableType = "Standard",
                        IsActivity = false, IsAuditEnabled = new(false), CanCreateCharts = new(true)
                    },
                    PrimaryAttribute = RelationText("fmc_name", "Name", 200)
                });
            var metadata = await RelationMetadata(table);
            await AddRelationComponent(metadata.MetadataId!.Value, 1);
            foreach (var column in RelationColumns(table))
                if (!metadata.Attributes.Any(a => a.LogicalName == column.SchemaName))
                    await client.ExecuteAsync(new CreateAttributeRequest
                        { EntityName = table, Attribute = column, SolutionUniqueName = Solution });
            var keyColumn = table == BuildingTable ? "fmc_buildingcode" : "fmc_equipmentcode";
            var keyName = table + (table == BuildingTable ? "_buildingcode" : "_equipmentcode");
            if (!(metadata.Keys ?? []).Any(k => k.LogicalName == keyName))
                await client.ExecuteAsync(new CreateEntityKeyRequest
                {
                    EntityName = table, SolutionUniqueName = Solution,
                    EntityKey = new EntityKeyMetadata
                        { SchemaName = keyName, DisplayName = new Label(label + " Code", 1033), KeyAttributes = [keyColumn] }
                });
            Console.WriteLine($"Schema ready: {table}.");
        }
        await EnsureBmsRelationship(BuildingTable, EquipmentTable, "fmc_buildingid", "Building", BuildingRelationship, true);
        await EnsureBmsRelationship(EquipmentTable, PointTable, "fmc_equipmentid", "Equipment", EquipmentRelationship, false);
        await PublishBmsRelations();
        for (var attempt = 0; ; attempt++)
        {
            var keys = new List<EntityKeyMetadata>();
            foreach (var table in new[] { BuildingTable, EquipmentTable })
                keys.AddRange((await RelationMetadata(table)).Keys ?? []);
            if (keys.Any(k => k.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed))
                throw new InvalidOperationException("A BMS catalog alternate-key index failed. Inspect its async job.");
            if (keys.Count >= 2 && keys.All(k => k.EntityKeyIndexStatus == EntityKeyIndexStatus.Active)) break;
            if (attempt == 30) throw new TimeoutException("BMS keys are not Active after 150 seconds; rerun after checking metadata.");
            Console.WriteLine("Waiting for BMS catalog key indexes...");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        await ConfigureBmsRelationUi();
        await EnsureBmsRelationRolePrivileges();
        await PublishBmsRelations();
        await ReadBmsRelationMetadata(true);
        await VerifyBmsRelationUi();
        Console.WriteLine("BMS relationship schema, UI and existing runtime-role privileges published and verified.");
    }

    private async Task EnsureBmsRelationRolePrivileges()
    {
        var client = connection.Get();
        var rootQuery = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid") };
        rootQuery.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
        var root = (await client.RetrieveMultipleAsync(rootQuery)).Entities.Single().Id;
        var roleQuery = Query("role", "name", "FM Central BMS Integration");
        roleQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, root);
        var role = (await client.RetrieveMultipleAsync(roleQuery)).Entities.SingleOrDefault()
            ?? throw new InvalidOperationException("Existing runtime role FM Central BMS Integration was not found.");
        var privileges = new List<RolePrivilege>();
        foreach (var table in RelationTables)
        {
            var meta = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
                { LogicalName = table, EntityFilters = EntityFilters.Privileges })).EntityMetadata;
            privileges.AddRange(meta.Privileges
                .Where(p => p.PrivilegeType is PrivilegeType.Create or PrivilegeType.Read or PrivilegeType.Write or
                    PrivilegeType.Append or PrivilegeType.AppendTo)
                .Select(p => new RolePrivilege { PrivilegeId = p.PrivilegeId, Depth = PrivilegeDepth.Global }));
        }
        await client.ExecuteAsync(new AddPrivilegesRoleRequest { RoleId = role.Id, Privileges = privileges.ToArray() });
        Console.WriteLine($"Runtime role catalog/lookup privileges ready: {role.Id}.");
    }

    private async Task EnsureBmsRelationship(string parent, string child, string lookup, string label, string name, bool required)
    {
        var metadata = await RelationMetadata(child);
        if (metadata.ManyToOneRelationships.Any(r => r.SchemaName == name)) return;
        var eligibleParent = (CanBeReferencedResponse)await connection.Get().ExecuteAsync(new CanBeReferencedRequest { EntityName = parent });
        var eligibleChild = (CanBeReferencingResponse)await connection.Get().ExecuteAsync(new CanBeReferencingRequest { EntityName = child });
        if (!eligibleParent.CanBeReferenced || !eligibleChild.CanBeReferencing)
            throw new InvalidOperationException($"Tables are not eligible for relationship {name}.");
        await connection.Get().ExecuteAsync(new CreateOneToManyRequest
        {
            SolutionUniqueName = Solution,
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = lookup, DisplayName = new Label(label, 1033),
                RequiredLevel = new(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
            },
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = name, ReferencedEntity = parent, ReferencingEntity = child,
                AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                    { Behavior = AssociatedMenuBehavior.UseLabel, Group = AssociatedMenuGroup.Details, Label = new Label(child == PointTable ? "BMS Points" : "BMS Equipment", 1033), Order = 10000 },
                CascadeConfiguration = new CascadeConfiguration
                {
                    Assign = CascadeType.NoCascade, Delete = CascadeType.Restrict, Merge = CascadeType.NoCascade,
                    Reparent = CascadeType.NoCascade, Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade
                }
            }
        });
        Console.WriteLine($"Created lookup relationship: {name}.");
    }

    private async Task<EntityMetadata> RelationMetadata(string table) =>
        ((RetrieveEntityResponse)await connection.Get().ExecuteAsync(new RetrieveEntityRequest
            { LogicalName = table, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true })).EntityMetadata;

    private async Task AddRelationComponent(Guid id, int type) => await connection.Get().ExecuteAsync(new AddSolutionComponentRequest
        { ComponentId = id, ComponentType = type, SolutionUniqueName = Solution, AddRequiredComponents = false });

    private async Task RequireSolutionComponent(Guid id, int type)
    {
        var q = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet(false), TopCount = 1 };
        q.Criteria.AddCondition("objectid", ConditionOperator.Equal, id);
        q.Criteria.AddCondition("componenttype", ConditionOperator.Equal, type);
        q.AddLink("solution", "solutionid", "solutionid").LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, Solution);
        if ((await connection.Get().RetrieveMultipleAsync(q)).Entities.Count != 1)
            throw new InvalidOperationException($"Component {id} ({type}) is missing from {Solution}.");
    }

    private async Task PublishBmsRelations() => await connection.Get().ExecuteAsync(new PublishXmlRequest
    {
        ParameterXml = new XElement("importexportxml", new XElement("entities",
            RelationTables.Select(t => new XElement("entity", t)))).ToString(SaveOptions.DisableFormatting)
    });
}
