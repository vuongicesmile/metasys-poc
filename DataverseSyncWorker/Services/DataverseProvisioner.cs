using DataverseSyncWorker.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

/// <summary>Explicit deployment command; never runs as part of ordinary synchronization.</summary>
public sealed partial class DataverseProvisioner(DataverseConnection connection, SyncOptions options)
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
        foreach (var (table, label, elastic, columns) in new[]
        {
            ("fmc_bmspoint", "BMS Point", false, Columns(false)),
            ("fmc_bmsreading", "BMS Reading", true, Columns(true)),
            ("fmc_syncrequest", "Sync Request", false, SyncRequestColumns())
        })
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
            foreach (var attr in columns)
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
        var requestMeta = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = "fmc_syncrequest", EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
        })).EntityMetadata;
        if (!(requestMeta.Keys ?? []).Any(k => k.LogicalName == "fmc_syncrequest_correlationid"))
            await client.ExecuteAsync(new CreateEntityKeyRequest
            {
                EntityName = "fmc_syncrequest", SolutionUniqueName = Solution,
                EntityKey = new EntityKeyMetadata
                {
                    SchemaName = "fmc_syncrequest_correlationid", DisplayName = new Label("Sync Correlation ID", 1033),
                    KeyAttributes = ["fmc_correlationid"]
                }
            });
        // Elastic uses its built-in primary GUID + partitionid key only.
        await ConfigureDefaultView(client, "fmc_bmspoint", "Active BMS Points",
            ["fmc_objectid", "fmc_name", "fmc_objecttype", "fmc_currentvalue", "fmc_unit", "fmc_lastreadingtime", "fmc_building"]);
        await ConfigureDefaultView(client, "fmc_bmsreading", "All BMS Readings",
            ["fmc_objectid", "fmc_objectname", "fmc_objecttype", "fmc_readingvalue", "fmc_unit", "fmc_readingtime", "fmc_building"]);
        await ConfigureDefaultView(client, "fmc_syncrequest", "Active Sync Requests",
            ["fmc_name", "fmc_status", "fmc_requestedcutoffid", "fmc_startedat", "fmc_completedat", "fmc_deliveredrows", "fmc_pendingafter"]);
        await EnsureRequestView(client, "Queued or Running Sync Requests",
            $"<condition attribute=\"fmc_status\" operator=\"in\"><value>{SyncRequestStatuses.Queued}</value><value>{SyncRequestStatuses.Running}</value></condition>");
        await EnsureRequestView(client, "Failed Sync Requests",
            $"<condition attribute=\"fmc_status\" operator=\"eq\" value=\"{SyncRequestStatuses.Failed}\" />");
        await EnsureRequestView(client, "Completed Sync Requests",
            $"<condition attribute=\"fmc_status\" operator=\"in\"><value>{SyncRequestStatuses.Succeeded}</value><value>{SyncRequestStatuses.CompletedWithIssues}</value></condition>");
        await client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><entities><entity>fmc_bmspoint</entity><entity>fmc_bmsreading</entity><entity>fmc_syncrequest</entity></entities></importexportxml>"
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
        foreach (var table in new[] { "fmc_bmspoint", "fmc_bmsreading", "fmc_syncrequest" })
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
        await EnsureRequestorRole(client, root);
        Console.WriteLine($"Runtime role ready: {roleId}. Assign this role to the runtime application user; remove deployment roles before continuous sync.");
        if (requestMeta.IsOptimisticConcurrencyEnabled != true)
            throw new InvalidOperationException("fmc_syncrequest must support optimistic concurrency for atomic worker claims.");
        if (options.ProvisionPowerAutomate)
            await new PowerAutomateProvisioner(connection, options).Run();
        Console.WriteLine("Provisioning complete. Export with pac solution export --name FMCentralBms --path dataverse/FMCentralBms.zip");
    }

    private static async Task EnsureRequestView(ServiceClient client, string name, string condition)
    {
        var metadata = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = "fmc_syncrequest", EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata;
        var objectTypeCode = metadata.ObjectTypeCode!.Value;
        var query = Query("savedquery", "name", name);
        query.ColumnSet = new ColumnSet("savedqueryid");
        query.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, objectTypeCode);
        var existing = (await client.RetrieveMultipleAsync(query)).Entities.FirstOrDefault();
        var columns = new[] { "fmc_name", "fmc_status", "fmc_requestedcutoffid", "fmc_startedat", "fmc_completedat", "fmc_deliveredrows", "fmc_pendingafter" };
        var fetchColumns = string.Join("", new[] { "fmc_syncrequestid" }.Concat(columns).Select(c => $"<attribute name=\"{c}\" />"));
        var cells = string.Join("", columns.Select((c, i) => $"<cell name=\"{c}\" width=\"{(i == 0 ? 260 : 140)}\" />"));
        var values = new Entity("savedquery")
        {
            ["name"] = name,
            ["returnedtypecode"] = objectTypeCode,
            ["querytype"] = 0,
            ["fetchxml"] = $"<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"fmc_syncrequest\">{fetchColumns}<filter type=\"and\">{condition}</filter><order attribute=\"createdon\" descending=\"true\" /></entity></fetch>",
            ["layoutxml"] = $"<grid name=\"resultset\" object=\"{objectTypeCode}\" jump=\"fmc_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"fmc_syncrequestid\">{cells}</row></grid>"
        };
        Guid id;
        if (existing is null) id = await client.CreateAsync(values);
        else
        {
            var update = new Entity("savedquery", existing.Id)
            {
                ["fetchxml"] = values["fetchxml"], ["layoutxml"] = values["layoutxml"]
            };
            await client.UpdateAsync(update); id = existing.Id;
        }
        await client.ExecuteAsync(new AddSolutionComponentRequest
        {
            ComponentId = id, ComponentType = 26, SolutionUniqueName = Solution, AddRequiredComponents = false
        });
    }

    private static async Task EnsureRequestorRole(ServiceClient client, Guid rootBusinessUnit)
    {
        const string name = "FM Central BMS Sync Requestor";
        var roles = Query("role", "name", name);
        roles.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, rootBusinessUnit);
        var existing = await client.RetrieveMultipleAsync(roles);
        var roleId = existing.Entities.FirstOrDefault()?.Id ?? await client.CreateAsync(new Entity("role")
        {
            ["name"] = name, ["businessunitid"] = new EntityReference("businessunit", rootBusinessUnit)
        });
        var meta = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = "fmc_syncrequest", EntityFilters = EntityFilters.Privileges
        })).EntityMetadata;
        var privileges = meta.Privileges
            .Where(p => p.PrivilegeType is PrivilegeType.Create or PrivilegeType.Read)
            .Select(p => new RolePrivilege { PrivilegeId = p.PrivilegeId, Depth = PrivilegeDepth.Global }).ToArray();
        await client.ExecuteAsync(new AddPrivilegesRoleRequest { RoleId = roleId, Privileges = privileges });
        await client.ExecuteAsync(new AddSolutionComponentRequest
        {
            ComponentId = roleId, ComponentType = 20, SolutionUniqueName = Solution, AddRequiredComponents = false
        });
        Console.WriteLine($"Requestor role ready: {roleId}.");
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
            ["fetchxml"] = $"<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"{table}\">{fetchColumns}<order attribute=\"{(table == "fmc_bmspoint" ? "fmc_name" : table == "fmc_bmsreading" ? "fmc_readingtime" : "createdon")}\" descending=\"{(table == "fmc_bmspoint" ? "false" : "true")}\" /></entity></fetch>",
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

    public static IEnumerable<AttributeMetadata> SyncRequestColumns()
    {
        yield return Text("fmc_command", "Command", 50);
        yield return Text("fmc_pipeline", "Pipeline", 150);
        yield return Text("fmc_correlationid", "Correlation ID", 100);
        yield return Text("fmc_requestedby", "Requested By", 200);
        yield return Text("fmc_requestedcutoffid", "Requested Cutoff SQL ID", 20);
        yield return Text("fmc_workerowner", "Worker Owner", 200);
        yield return new PicklistAttributeMetadata
        {
            SchemaName = "fmc_status", DisplayName = new Label("Sync Status", 1033),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            DefaultFormValue = SyncRequestStatuses.Queued,
            OptionSet = new OptionSetMetadata
            {
                IsGlobal = false,
                Options =
                {
                    new OptionMetadata { Label = new Label("Queued", 1033), Value = SyncRequestStatuses.Queued },
                    new OptionMetadata { Label = new Label("Running", 1033), Value = SyncRequestStatuses.Running },
                    new OptionMetadata { Label = new Label("Succeeded", 1033), Value = SyncRequestStatuses.Succeeded },
                    new OptionMetadata { Label = new Label("Completed with issues", 1033), Value = SyncRequestStatuses.CompletedWithIssues },
                    new OptionMetadata { Label = new Label("Failed", 1033), Value = SyncRequestStatuses.Failed }
                }
            }
        };
        foreach (var (name, label) in new[]
        {
            ("fmc_startedat", "Started At"), ("fmc_completedat", "Completed At"),
            ("fmc_heartbeat", "Heartbeat"), ("fmc_leaseexpiresat", "Lease Expires At")
        }) yield return Time(name, label);
        foreach (var (name, label) in new[]
        {
            ("fmc_baselinedelivered", "Baseline Delivered"), ("fmc_baselinedeadletters", "Baseline Dead Letters"),
            ("fmc_deliveredrows", "Delivered Rows"), ("fmc_quarantinedrows", "Quarantined Rows"),
            ("fmc_pendingafter", "Pending After"), ("fmc_deadletterafter", "Dead Letters After")
        }) yield return new BigIntAttributeMetadata { SchemaName = name, DisplayName = new Label(label, 1033) };
        yield return new IntegerAttributeMetadata
        {
            SchemaName = "fmc_batches", DisplayName = new Label("Batches", 1033), MinValue = 0, MaxValue = int.MaxValue
        };
        yield return new IntegerAttributeMetadata
        {
            SchemaName = "fmc_attempts", DisplayName = new Label("Claim Attempts", 1033), MinValue = 0, MaxValue = int.MaxValue
        };
        yield return new MemoAttributeMetadata
        {
            SchemaName = "fmc_errormessage", DisplayName = new Label("Error Message", 1033), MaxLength = 4000
        };
    }
}
