using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public sealed partial class DataverseProvisioner
{
    public const string SpoFileTable = "fmc_spofile";
    public const string SpoImportRowTable = "fmc_spoimportrow";
    public const string SpoFileSourceKey = "fmc_spofile_sourcekey";
    public const string SpoImportRowKey = "fmc_spoimportrow_versionordinal";
    public const string SpoFileImportRelationship = "fmc_spofile_spoimportrow";

    private const string DemoAppUniqueName = "fmc_FMCBMSDemo";
    private const string DemoSiteMapName = "fmc_FMCBMSDemo";
    private const string IngestionRoleName = "FM Central SPO File Ingestion";

    private static readonly (string SchemaName, string DisplayName, string? DefaultValue)[] SpoVariables =
    [
        ("fmc_SpoSiteUrl", "FMC SPO Site URL", null),
        ("fmc_SpoLibraryId", "FMC SPO Library ID", null),
        ("fmc_SpoInboxPath", "FMC SPO Inbox Path", null),
        ("fmc_SpoSourceNamespace", "FMC SPO Source Namespace", "bms_spo_dev01")
    ];

    public async Task PrintSpoIngestionStatus()
    {
        var client = connection.Get();
        var all = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(new RetrieveAllEntitiesRequest
        {
            EntityFilters = EntityFilters.Entity
        });
        var tables = all.EntityMetadata.Where(e => e.LogicalName is SpoFileTable or SpoImportRowTable)
            .Select(e => new { e.LogicalName, e.TableType, Ownership = e.OwnershipType.ToString() })
            .OrderBy(e => e.LogicalName).ToArray();
        var variables = await FindEnvironmentVariables(client);
        var apps = await client.RetrieveMultipleAsync(Query("appmodule", "uniquename", DemoAppUniqueName));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Environment = options.Url,
            Solution,
            Tables = tables,
            EnvironmentVariables = variables.Select(v => v.GetAttributeValue<string>("schemaname")).OrderBy(x => x),
            DemoAppCount = apps.Entities.Count
        }));
    }

    public async Task ProvisionSpoIngestion()
    {
        var client = connection.Get();
        await RequireSpoSolution(client);

        await EnsureSpoTable(client, SpoFileTable, "SPO File", "SPO Files", SpoFileColumns());
        await EnsureSpoTable(client, SpoImportRowTable, "SPO Import Row", "SPO Import Rows", SpoImportColumns());
        await EnsureSpoImportRelationship(client);
        await EnsureSpoKey(client, SpoFileTable, SpoFileSourceKey, "SPO Source Key", ["fmc_sourcekey"]);
        await EnsureSpoKey(client, SpoImportRowTable, SpoImportRowKey, "SPO File Version Ordinal",
            ["fmc_fileid", "fmc_sourceetag", "fmc_ordinal"]);

        await PublishSpoTables(client);
        await WaitForSpoKeys(client);

        var allRowsView = await EnsureSpoView(client, SpoImportRowTable, "All SPO Import Rows",
            ["fmc_name", "fmc_fileid", "fmc_sourceetag", "fmc_ordinal", "fmc_code", "fmc_buildingname", "fmc_floorcount"], null);
        foreach (var view in SpoFileViews())
            await EnsureSpoView(client, SpoFileTable, view.Name, view.Columns, view.Condition);
        await ConfigureSpoForms(client, allRowsView);
        await EnsureSpoEnvironmentVariables(client);
        await EnsureSpoIngestionRole(client);
        await AddSpoFileToDemoApp(client);
        await PublishSpoTables(client, includeApp: true);
        await VerifySpoIngestion();
        Console.WriteLine("SPO file-ingestion schema, keys, UI, app navigation, variables and runtime role are ready.");
    }

    public async Task VerifySpoIngestion()
    {
        var client = connection.Get();
        await RequireSpoSolution(client);
        var file = await ReadSpoMetadata(client, SpoFileTable, EntityFilters.All);
        var rows = await ReadSpoMetadata(client, SpoImportRowTable, EntityFilters.All);

        RequireStandardOrganizationTable(file);
        RequireStandardOrganizationTable(rows);
        RequireAttribute<FileAttributeMetadata>(file, "fmc_file", a => a.MaxSizeInKB == 5120, "MaxSizeInKB=5120");
        RequireAttribute<StringAttributeMetadata>(file, "fmc_sourcekey",
            a => a.MaxLength == 150 && a.RequiredLevel.Value == AttributeRequiredLevel.ApplicationRequired,
            "Text(150), ApplicationRequired");
        RequireAttribute<StringAttributeMetadata>(file, "fmc_filename",
            a => a.MaxLength == 255 && a.RequiredLevel.Value == AttributeRequiredLevel.ApplicationRequired,
            "Text(255), ApplicationRequired");
        RequireAttribute<LookupAttributeMetadata>(rows, "fmc_fileid",
            a => a.RequiredLevel.Value == AttributeRequiredLevel.ApplicationRequired, "Lookup, ApplicationRequired");
        RequireAttribute<IntegerAttributeMetadata>(rows, "fmc_ordinal", a => a.MinValue == 1 && a.MaxValue == 100,
            "Whole Number 1..100");

        RequireActiveKey(file, SpoFileSourceKey, ["fmc_sourcekey"]);
        RequireActiveKey(rows, SpoImportRowKey, ["fmc_fileid", "fmc_sourceetag", "fmc_ordinal"]);
        if (!(rows.ManyToOneRelationships ?? []).Any(r => r.SchemaName == SpoFileImportRelationship &&
                r.ReferencedEntity == SpoFileTable && r.ReferencingAttribute == "fmc_fileid"))
            throw new InvalidOperationException("SPO import-row lookup relationship is missing or differs from the contract.");

        foreach (var view in SpoFileViews().Select(v => v.Name).Append("All SPO Import Rows"))
            if ((await FindSpoViews(client, view)).Count != 1)
                throw new InvalidOperationException($"Expected exactly one public view named '{view}'.");

        foreach (var table in new[] { SpoFileTable, SpoImportRowTable })
        foreach (var form in await SpoMainForms(client, table))
        {
            var xml = XElement.Parse(form.GetAttributeValue<string>("formxml"));
            var required = table == SpoFileTable
                ? new[] { "fmc_name", "fmc_filename", "fmc_status", "fmc_importstatus", "fmc_file" }
                : new[] { "fmc_name", "fmc_fileid", "fmc_sourceetag", "fmc_ordinal", "fmc_code" };
            if (required.Any(field => !xml.Descendants("control").Any(c => (string?)c.Attribute("datafieldname") == field)))
                throw new InvalidOperationException($"Main form is missing required SPO controls: {table}/{form.Id}.");
        }

        var variables = await FindEnvironmentVariables(client);
        foreach (var variable in SpoVariables)
            if (!variables.Any(v => v.GetAttributeValue<string>("schemaname") == variable.SchemaName))
                throw new InvalidOperationException($"Environment variable definition missing: {variable.SchemaName}.");

        var app = await FindSingle(client, "appmodule", "uniquename", DemoAppUniqueName,
            new ColumnSet("appmoduleid"));
        var site = await FindSingle(client, "sitemap", "sitemapname", DemoSiteMapName,
            new ColumnSet("sitemapid", "sitemapxml"));
        if (!XElement.Parse(site.GetAttributeValue<string>("sitemapxml")).Descendants("SubArea")
                .Any(x => (string?)x.Attribute("Id") == "fmc_spofiles" && (string?)x.Attribute("Entity") == SpoFileTable))
            throw new InvalidOperationException("FMC BMS Demo sitemap does not contain the SPO Files page.");
        await RequireSolutionComponent(client, app.Id, 80);
        await RequireSolutionComponent(client, site.Id, 62);
        await RequireSolutionComponent(client, file.MetadataId!.Value, 1);
        await RequireSolutionComponent(client, rows.MetadataId!.Value, 1);
        Console.WriteLine("Verified SPO ingestion metadata, Active keys, relationship, views, forms, variables, sitemap and solution membership.");
    }

    private async Task RequireSpoSolution(ServiceClient client)
    {
        var q = new QueryExpression("solution") { ColumnSet = new ColumnSet("solutionid", "ismanaged", "publisherid"), TopCount = 2 };
        q.Criteria.AddCondition("uniquename", ConditionOperator.Equal, Solution);
        var solutions = (await client.RetrieveMultipleAsync(q)).Entities;
        if (solutions.Count != 1 || solutions[0].GetAttributeValue<bool>("ismanaged"))
            throw new InvalidOperationException("Expected one unmanaged FMCentralBms solution; no SPO changes were made.");
        var publisher = await client.RetrieveAsync("publisher", solutions[0].GetAttributeValue<EntityReference>("publisherid").Id,
            new ColumnSet("uniquename", "customizationprefix"));
        if (publisher.GetAttributeValue<string>("uniquename") != "FMCentralBmsPublisher" ||
            publisher.GetAttributeValue<string>("customizationprefix") != "fmc")
            throw new InvalidOperationException("FMCentralBms publisher/prefix differs from the repository contract.");
    }

    private async Task EnsureSpoTable(ServiceClient client, string table, string singular, string plural,
        IEnumerable<AttributeMetadata> columns)
    {
        var all = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(new RetrieveAllEntitiesRequest
        {
            EntityFilters = EntityFilters.Entity
        });
        var existing = all.EntityMetadata.SingleOrDefault(e => e.LogicalName == table);
        if (existing is not null && (existing.TableType != "Standard" || existing.OwnershipType != OwnershipTypes.OrganizationOwned))
            throw new InvalidOperationException($"{table} exists with an incompatible type/ownership; refusing migration by retry.");
        if (existing is null)
        {
            await client.ExecuteAsync(new CreateEntityRequest
            {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata
                {
                    SchemaName = table,
                    DisplayName = new Label(singular, 1033),
                    DisplayCollectionName = new Label(plural, 1033),
                    OwnershipType = OwnershipTypes.OrganizationOwned,
                    TableType = "Standard",
                    IsActivity = false,
                    IsAuditEnabled = new BooleanManagedProperty(false),
                    CanCreateCharts = new BooleanManagedProperty(true)
                },
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = "fmc_name",
                    DisplayName = new Label("Name", 1033),
                    MaxLength = 255,
                    FormatName = StringFormatName.Text,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
                }
            });
            Console.WriteLine($"Created table: {table}.");
        }

        var metadata = await ReadSpoMetadata(client, table, EntityFilters.Entity | EntityFilters.Attributes);
        await AddSpoSolutionComponent(client, metadata.MetadataId!.Value, 1, false);
        foreach (var attribute in columns)
        {
            var present = metadata.Attributes.SingleOrDefault(a => a.LogicalName == attribute.SchemaName);
            if (present is null)
            {
                await client.ExecuteAsync(new CreateAttributeRequest
                {
                    EntityName = table,
                    Attribute = attribute,
                    SolutionUniqueName = Solution
                });
                Console.WriteLine($"Created column: {table}.{attribute.SchemaName}.");
            }
            else if (present.GetType() != attribute.GetType())
                throw new InvalidOperationException($"{table}.{attribute.SchemaName} has an incompatible type.");
        }
    }

    private async Task EnsureSpoImportRelationship(ServiceClient client)
    {
        var rowMetadata = await ReadSpoMetadata(client, SpoImportRowTable, EntityFilters.All);
        if ((rowMetadata.ManyToOneRelationships ?? []).Any(r => r.SchemaName == SpoFileImportRelationship)) return;
        await client.ExecuteAsync(new CreateOneToManyRequest
        {
            SolutionUniqueName = Solution,
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = "fmc_fileid",
                DisplayName = new Label("SPO File", 1033),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired)
            },
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = SpoFileImportRelationship,
                ReferencedEntity = SpoFileTable,
                ReferencingEntity = SpoImportRowTable,
                AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                {
                    Behavior = AssociatedMenuBehavior.UseLabel,
                    Group = AssociatedMenuGroup.Details,
                    Label = new Label("Imported Rows", 1033),
                    Order = 10000
                },
                CascadeConfiguration = new CascadeConfiguration
                {
                    Assign = CascadeType.NoCascade,
                    Delete = CascadeType.Restrict,
                    Merge = CascadeType.NoCascade,
                    Reparent = CascadeType.NoCascade,
                    Share = CascadeType.NoCascade,
                    Unshare = CascadeType.NoCascade
                }
            }
        });
        Console.WriteLine($"Created relationship: {SpoFileImportRelationship}.");
    }

    private async Task EnsureSpoKey(ServiceClient client, string table, string logicalName, string label, string[] attributes)
    {
        var metadata = await ReadSpoMetadata(client, table, EntityFilters.All);
        var existing = (metadata.Keys ?? []).SingleOrDefault(k => k.LogicalName == logicalName);
        if (existing is not null)
        {
            if (!existing.KeyAttributes.OrderBy(x => x).SequenceEqual(attributes.OrderBy(x => x)))
                throw new InvalidOperationException($"Alternate key {logicalName} differs from the required columns.");
            return;
        }
        await client.ExecuteAsync(new CreateEntityKeyRequest
        {
            EntityName = table,
            SolutionUniqueName = Solution,
            EntityKey = new EntityKeyMetadata
            {
                SchemaName = logicalName,
                DisplayName = new Label(label, 1033),
                KeyAttributes = attributes
            }
        });
        Console.WriteLine($"Created alternate key: {logicalName}.");
    }

    private async Task WaitForSpoKeys(ServiceClient client)
    {
        for (var attempt = 0; attempt <= 30; attempt++)
        {
            var keys = new[]
            {
                (await ReadSpoMetadata(client, SpoFileTable, EntityFilters.All)).Keys!.Single(k => k.LogicalName == SpoFileSourceKey),
                (await ReadSpoMetadata(client, SpoImportRowTable, EntityFilters.All)).Keys!.Single(k => k.LogicalName == SpoImportRowKey)
            };
            if (keys.Any(k => k.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed))
                throw new InvalidOperationException("An SPO ingestion alternate-key index failed. Inspect the async job before retrying.");
            if (keys.All(k => k.EntityKeyIndexStatus == EntityKeyIndexStatus.Active)) return;
            if (attempt == 30) throw new TimeoutException("SPO ingestion keys are not Active after 150 seconds.");
            Console.WriteLine("Waiting for SPO ingestion alternate-key indexes...");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task<Guid> EnsureSpoView(ServiceClient client, string table, string name, string[] columns, string? condition)
    {
        var metadata = await ReadSpoMetadata(client, table, EntityFilters.Entity);
        var matches = await FindSpoViews(client, name, table);
        if (matches.Count > 1) throw new InvalidOperationException($"Ambiguous public view: {table}/{name}.");
        var attributes = string.Join("", new[] { table + "id" }.Concat(columns).Select(c => $"<attribute name=\"{c}\" />"));
        var filter = string.IsNullOrWhiteSpace(condition) ? "" : $"<filter type=\"and\">{condition}</filter>";
        var cells = string.Join("", columns.Select((c, i) => $"<cell name=\"{c}\" width=\"{(i == 0 ? 260 : 150)}\" />"));
        var values = new Entity("savedquery")
        {
            ["name"] = name,
            ["returnedtypecode"] = metadata.ObjectTypeCode!.Value,
            ["querytype"] = 0,
            ["fetchxml"] = $"<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"{table}\">{attributes}{filter}<order attribute=\"createdon\" descending=\"true\" /></entity></fetch>",
            ["layoutxml"] = $"<grid name=\"resultset\" object=\"{metadata.ObjectTypeCode.Value}\" jump=\"fmc_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"{table}id\">{cells}</row></grid>"
        };
        Guid id;
        if (matches.Count == 0) id = await client.CreateAsync(values);
        else
        {
            id = matches[0].Id;
            await client.UpdateAsync(new Entity("savedquery", id)
            {
                ["fetchxml"] = values["fetchxml"],
                ["layoutxml"] = values["layoutxml"]
            });
        }
        await AddSpoSolutionComponent(client, id, 26, false);
        Console.WriteLine($"View ready: {table}/{name} ({id}).");
        return id;
    }

    private async Task<List<Entity>> FindSpoViews(ServiceClient client, string name, string? table = null)
    {
        var q = new QueryExpression("savedquery") { ColumnSet = new ColumnSet("savedqueryid", "name", "fetchxml", "layoutxml"), TopCount = 2 };
        q.Criteria.AddCondition("name", ConditionOperator.Equal, name);
        q.Criteria.AddCondition("querytype", ConditionOperator.Equal, 0);
        if (table is not null)
        {
            var metadata = await ReadSpoMetadata(client, table, EntityFilters.Entity);
            q.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, metadata.ObjectTypeCode!.Value);
        }
        return (await client.RetrieveMultipleAsync(q)).Entities.ToList();
    }

    private async Task ConfigureSpoForms(ServiceClient client, Guid importRowsView)
    {
        foreach (var table in new[] { SpoFileTable, SpoImportRowTable })
        foreach (var form in await SpoMainForms(client, table))
        {
            var original = form.GetAttributeValue<string>("formxml");
            var updated = BuildSpoForm(original, table, form.Id, importRowsView);
            if (!XNode.DeepEquals(XElement.Parse(original), XElement.Parse(updated)))
                await client.UpdateAsync(new Entity("systemform", form.Id) { ["formxml"] = updated });
            await AddSpoSolutionComponent(client, form.Id, 60, false);
        }
    }

    internal static string BuildSpoForm(string original, string table, Guid formId, Guid importRowsView)
    {
        var form = XElement.Parse(original);
        var tabs = form.Element("tabs") ?? throw new InvalidOperationException("Main form has no tabs element.");
        var tab = tabs.Elements("tab").SingleOrDefault(t => (string?)t.Attribute("name") == "fmc_spoingestion");
        string Id(string key) => ReadingMapper.StableGuid($"spo-form|{formId:D}|{key}").ToString("B");
        static XElement Labels(string text) => new("labels", new XElement("label",
            new XAttribute("description", text), new XAttribute("languagecode", "1033")));
        XElement? rows = tab?.Descendants("rows").FirstOrDefault();
        if (tab is null)
        {
            rows = new XElement("rows");
            tab = new XElement("tab", new XAttribute("name", "fmc_spoingestion"), new XAttribute("id", Id("tab")),
                new XAttribute("showlabel", "true"), new XAttribute("expanded", "true"), Labels("SPO File Ingestion"),
                new XElement("columns", new XElement("column", new XAttribute("width", "100%"),
                    new XElement("sections", new XElement("section", new XAttribute("name", "fmc_spoingestion_details"),
                        new XAttribute("id", Id("section")), new XAttribute("showlabel", "false"), new XAttribute("showbar", "false"),
                        new XAttribute("columns", "1"), Labels("SPO File Ingestion"), rows)))));
            tabs.Add(tab);
        }
        if (rows is null) throw new InvalidOperationException("Existing SPO ingestion tab has no rows.");

        var fields = table == SpoFileTable
            ? new[]
            {
                ("fmc_name", "Name", "text"), ("fmc_filename", "File Name", "text"),
                ("fmc_file", "Archived File", "file"), ("fmc_status", "Archive Status", "choice"),
                ("fmc_importstatus", "Import Status", "choice"), ("fmc_filesize", "File Size", "integer"),
                ("fmc_sourcekey", "Source Key", "text"), ("fmc_etag", "Archived ETag", "text"),
                ("fmc_candidateetag", "Candidate ETag", "text"), ("fmc_receivedat", "Received At", "datetime"),
                ("fmc_processedat", "Processed At", "datetime"), ("fmc_rowcount", "Imported Rows", "integer"),
                ("fmc_runid", "Flow Run ID", "text"), ("fmc_sharepointurl", "SharePoint URL", "text"),
                ("fmc_errormessage", "Error Message", "memo")
            }
            : new[]
            {
                ("fmc_name", "Name", "text"), ("fmc_fileid", "SPO File", "lookup"),
                ("fmc_sourceetag", "Source ETag", "text"), ("fmc_ordinal", "Ordinal", "integer"),
                ("fmc_code", "Code", "text"), ("fmc_buildingname", "Building Name", "text"),
                ("fmc_floorcount", "Floor Count", "integer")
            };
        foreach (var (name, label, kind) in fields)
        {
            if (form.Descendants("control").Any(c => (string?)c.Attribute("datafieldname") == name)) continue;
            var classId = kind switch
            {
                "choice" => "{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}",
                "lookup" => "{270BD3DB-D9AF-4782-9025-509E298DEC0A}",
                "datetime" => "{5B773807-9FB2-42DB-97C3-7A91EFF8ADFF}",
                "integer" => "{C6D124CA-7EDA-4A60-AEA9-7FB8D318B68F}",
                "memo" => "{E0DECE4B-6FC8-4A8F-A065-082708572369}",
                // Dataverse resolves the modern File control from the bound FileAttributeMetadata.
                "file" => "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}",
                _ => "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}"
            };
            var control = new XElement("control", new XAttribute("id", name), new XAttribute("classid", classId),
                new XAttribute("datafieldname", name));
            if (table == SpoFileTable && name is not ("fmc_name" or "fmc_file")) control.Add(new XAttribute("disabled", "true"));
            rows.Add(new XElement("row", new XElement("cell", new XAttribute("id", Id(name)), Labels(label), control)));
        }
        if (table == SpoFileTable && !form.Descendants("control").Any(c => (string?)c.Attribute("id") == "fmc_spoimportrows"))
        {
            rows.Add(new XElement("row", new XElement("cell", new XAttribute("id", Id("importrows")), Labels("Imported JSON Rows"),
                new XElement("control", new XAttribute("id", "fmc_spoimportrows"),
                    new XAttribute("classid", "{E7A81278-8635-4D9E-8D4D-59480B391C5B}"),
                    new XElement("parameters", new XElement("TargetEntityType", SpoImportRowTable),
                        new XElement("ViewId", importRowsView.ToString("B")), new XElement("IsUserView", "false"),
                        new XElement("RelationshipName", SpoFileImportRelationship), new XElement("RecordsPerPage", "10"),
                        new XElement("EnableQuickFind", "false"))))));
        }
        return form.ToString(SaveOptions.DisableFormatting);
    }

    private async Task<List<Entity>> SpoMainForms(ServiceClient client, string table)
    {
        var metadata = await ReadSpoMetadata(client, table, EntityFilters.Entity);
        var q = new QueryExpression("systemform") { ColumnSet = new ColumnSet("formid", "name", "formxml") };
        q.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, metadata.ObjectTypeCode!.Value);
        q.Criteria.AddCondition("type", ConditionOperator.Equal, 2);
        q.Criteria.AddCondition("formactivationstate", ConditionOperator.Equal, 1);
        var forms = (await client.RetrieveMultipleAsync(q)).Entities.ToList();
        if (forms.Count == 0) throw new InvalidOperationException($"No active main form exists for {table}.");
        return forms;
    }

    private async Task EnsureSpoEnvironmentVariables(ServiceClient client)
    {
        foreach (var variable in SpoVariables)
        {
            var q = Query("environmentvariabledefinition", "schemaname", variable.SchemaName);
            q.ColumnSet = new ColumnSet("environmentvariabledefinitionid", "type");
            var matches = (await client.RetrieveMultipleAsync(q)).Entities;
            if (matches.Count > 1) throw new InvalidOperationException($"Duplicate environment variable: {variable.SchemaName}.");
            Guid id;
            if (matches.Count == 0)
            {
                var values = new Entity("environmentvariabledefinition")
                {
                    ["displayname"] = variable.DisplayName,
                    ["schemaname"] = variable.SchemaName,
                    ["description"] = "SharePoint Online file-ingestion configuration. Bind a current value per environment before enabling flows.",
                    ["type"] = new OptionSetValue(100000000)
                };
                if (variable.DefaultValue is not null) values["defaultvalue"] = variable.DefaultValue;
                id = await client.CreateAsync(values);
            }
            else
            {
                if (matches[0].GetAttributeValue<OptionSetValue>("type")?.Value != 100000000)
                    throw new InvalidOperationException($"Environment variable {variable.SchemaName} is not Text.");
                id = matches[0].Id;
            }
            await AddSpoSolutionComponent(client, id, 380, false);
        }
    }

    private async Task<List<Entity>> FindEnvironmentVariables(ServiceClient client)
    {
        var names = SpoVariables.Select(v => v.SchemaName).ToArray();
        var q = new QueryExpression("environmentvariabledefinition")
        {
            ColumnSet = new ColumnSet("environmentvariabledefinitionid", "schemaname", "type", "defaultvalue")
        };
        q.Criteria.AddCondition("schemaname", ConditionOperator.In, names.Cast<object>().ToArray());
        return (await client.RetrieveMultipleAsync(q)).Entities.ToList();
    }

    private async Task EnsureSpoIngestionRole(ServiceClient client)
    {
        var rootQuery = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid") };
        rootQuery.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
        var root = (await client.RetrieveMultipleAsync(rootQuery)).Entities.Single().Id;
        var q = Query("role", "name", IngestionRoleName);
        q.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, root);
        var matches = (await client.RetrieveMultipleAsync(q)).Entities;
        if (matches.Count > 1) throw new InvalidOperationException($"Duplicate role: {IngestionRoleName}.");
        var roleId = matches.FirstOrDefault()?.Id ?? await client.CreateAsync(new Entity("role")
        {
            ["name"] = IngestionRoleName,
            ["businessunitid"] = new EntityReference("businessunit", root)
        });
        var privileges = new List<RolePrivilege>();
        foreach (var table in new[] { SpoFileTable, SpoImportRowTable })
        {
            var metadata = await ReadSpoMetadata(client, table, EntityFilters.Privileges);
            privileges.AddRange(metadata.Privileges
                .Where(p => p.PrivilegeType is PrivilegeType.Create or PrivilegeType.Read or PrivilegeType.Write or PrivilegeType.Append or PrivilegeType.AppendTo)
                .Select(p => new RolePrivilege { PrivilegeId = p.PrivilegeId, Depth = PrivilegeDepth.Global }));
        }
        await client.ExecuteAsync(new AddPrivilegesRoleRequest { RoleId = roleId, Privileges = privileges.ToArray() });
        await AddSpoSolutionComponent(client, roleId, 20, false);
        Console.WriteLine($"SPO ingestion runtime role ready: {roleId}. No user or application-user assignment was changed.");
    }

    private async Task AddSpoFileToDemoApp(ServiceClient client)
    {
        var app = await FindSingle(client, "appmodule", "uniquename", DemoAppUniqueName,
            new ColumnSet("appmoduleid"));
        var site = await FindSingle(client, "sitemap", "sitemapname", DemoSiteMapName,
            new ColumnSet("sitemapid", "sitemapxml"));
        var xml = XElement.Parse(site.GetAttributeValue<string>("sitemapxml"));
        var group = xml.Descendants("Group").FirstOrDefault(g => (string?)g.Attribute("Id") == "fmc_catalog")
            ?? throw new InvalidOperationException("FMC BMS Demo catalog group is missing; refusing to replace the sitemap.");
        var subArea = group.Elements("SubArea").SingleOrDefault(x => (string?)x.Attribute("Id") == "fmc_spofiles");
        if (subArea is null)
        {
            subArea = new XElement("SubArea", new XAttribute("Id", "fmc_spofiles"), new XAttribute("Entity", SpoFileTable),
                new XElement("Titles", new XElement("Title", new XAttribute("LCID", "1033"), new XAttribute("Title", "SPO Files"))));
            group.Add(subArea);
            await client.UpdateAsync(new Entity("sitemap", site.Id) { ["sitemapxml"] = xml.ToString(SaveOptions.DisableFormatting) });
        }
        else if ((string?)subArea.Attribute("Entity") != SpoFileTable)
            throw new InvalidOperationException("Existing fmc_spofiles sitemap entry targets another component.");

        await AddSpoSolutionComponent(client, site.Id, 62, false);
        await AddSpoSolutionComponent(client, app.Id, 80, false);
        var metadata = await ReadSpoMetadata(client, SpoFileTable, EntityFilters.Entity);
        var components = new EntityReferenceCollection
        {
            new(SpoFileTable, metadata.MetadataId!.Value)
        };
        foreach (var viewName in SpoFileViews().Select(v => v.Name))
        foreach (var view in await FindSpoViews(client, viewName, SpoFileTable))
            components.Add(view.ToEntityReference());
        foreach (var form in await SpoMainForms(client, SpoFileTable))
            components.Add(form.ToEntityReference());
        var importRowMetadata = await ReadSpoMetadata(client, SpoImportRowTable, EntityFilters.Entity);
        components.Add(new EntityReference(SpoImportRowTable, importRowMetadata.MetadataId!.Value));
        foreach (var view in await FindSpoViews(client, "All SPO Import Rows", SpoImportRowTable))
            components.Add(view.ToEntityReference());
        var add = new OrganizationRequest("AddAppComponents");
        add.Parameters["AppId"] = app.Id;
        add.Parameters["Components"] = components;
        await client.ExecuteAsync(add);
        Console.WriteLine("SPO Files navigation added to FMC BMS Demo.");
    }

    private async Task<Entity> FindSingle(ServiceClient client, string table, string column, string value, ColumnSet columns)
    {
        var q = new QueryExpression(table) { ColumnSet = columns, TopCount = 2 };
        q.Criteria.AddCondition(column, ConditionOperator.Equal, value);
        var rows = (await client.RetrieveMultipleAsync(q)).Entities;
        return rows.Count == 1 ? rows[0] : throw new InvalidOperationException($"Expected one {table} where {column}={value}.");
    }

    private async Task AddSpoSolutionComponent(ServiceClient client, Guid id, int type, bool required) =>
        await client.ExecuteAsync(new AddSolutionComponentRequest
        {
            ComponentId = id,
            ComponentType = type,
            SolutionUniqueName = Solution,
            AddRequiredComponents = required
        });

    private async Task RequireSolutionComponent(ServiceClient client, Guid id, int type)
    {
        var q = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet(false), TopCount = 1 };
        q.Criteria.AddCondition("objectid", ConditionOperator.Equal, id);
        q.Criteria.AddCondition("componenttype", ConditionOperator.Equal, type);
        q.AddLink("solution", "solutionid", "solutionid").LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, Solution);
        if ((await client.RetrieveMultipleAsync(q)).Entities.Count != 1)
            throw new InvalidOperationException($"Component {id} ({type}) is missing from {Solution}.");
    }

    private async Task PublishSpoTables(ServiceClient client, bool includeApp = false)
    {
        var app = includeApp ? await FindSingle(client, "appmodule", "uniquename", DemoAppUniqueName, new ColumnSet("appmoduleid")) : null;
        var site = includeApp ? await FindSingle(client, "sitemap", "sitemapname", DemoSiteMapName, new ColumnSet("sitemapid")) : null;
        var extra = includeApp
            ? $"<sitemaps><sitemap>{site!.Id}</sitemap></sitemaps><appmodules><appmodule>{app!.Id}</appmodule></appmodules>"
            : "";
        await client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{SpoFileTable}</entity><entity>{SpoImportRowTable}</entity></entities>{extra}</importexportxml>"
        });
    }

    private static IEnumerable<AttributeMetadata> SpoFileColumns()
    {
        yield return SpoText("fmc_sourcekey", "Source Key", 150, true);
        yield return SpoText("fmc_filename", "File Name", 255, true);
        yield return SpoText("fmc_sharepointidentifier", "SharePoint Identifier", 4000);
        yield return SpoText("fmc_sharepointpath", "SharePoint Path", 4000);
        yield return SpoText("fmc_sharepointurl", "SharePoint URL", 4000);
        yield return SpoInteger("fmc_filesize", "File Size", 0, 5242880);
        yield return new FileAttributeMetadata
        {
            SchemaName = "fmc_file",
            DisplayName = new Label("Archived File", 1033),
            MaxSizeInKB = 5120,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
        };
        yield return SpoChoice("fmc_status", "Archive Status", SpoArchiveStatuses.Received,
            ("Received", SpoArchiveStatuses.Received), ("Processing", SpoArchiveStatuses.Processing),
            ("Archived", SpoArchiveStatuses.Archived), ("Failed", SpoArchiveStatuses.Failed),
            ("Ignored", SpoArchiveStatuses.Ignored));
        yield return SpoText("fmc_etag", "Archived ETag", 200);
        yield return SpoText("fmc_candidateetag", "Candidate ETag", 200);
        yield return Time("fmc_receivedat", "Received At");
        yield return Time("fmc_processedat", "Processed At");
        yield return SpoText("fmc_runid", "Flow Run ID", 100);
        yield return new MemoAttributeMetadata { SchemaName = "fmc_errormessage", DisplayName = new Label("Error Message", 1033), MaxLength = 4000 };
        yield return SpoChoice("fmc_importstatus", "Import Status", SpoImportStatuses.NotRequested,
            ("Not Requested", SpoImportStatuses.NotRequested), ("Processing", SpoImportStatuses.Processing),
            ("Imported", SpoImportStatuses.Imported), ("Failed", SpoImportStatuses.Failed));
        yield return SpoText("fmc_importedetag", "Imported ETag", 200);
        yield return SpoInteger("fmc_rowcount", "Imported Row Count", 0, int.MaxValue);
    }

    private static IEnumerable<AttributeMetadata> SpoImportColumns()
    {
        yield return SpoText("fmc_sourceetag", "Source ETag", 200, true);
        yield return SpoInteger("fmc_ordinal", "Ordinal", 1, 100, true);
        yield return SpoText("fmc_code", "Code", 50);
        yield return SpoText("fmc_buildingname", "Building Name", 255);
        yield return SpoInteger("fmc_floorcount", "Floor Count", 0, 200);
    }

    private static StringAttributeMetadata SpoText(string name, string label, int maxLength, bool required = false) => new()
    {
        SchemaName = name,
        DisplayName = new Label(label, 1033),
        MaxLength = maxLength,
        FormatName = StringFormatName.Text,
        RequiredLevel = new AttributeRequiredLevelManagedProperty(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
    };

    private static IntegerAttributeMetadata SpoInteger(string name, string label, int min, int max,
        bool required = false) => new()
    {
        SchemaName = name,
        DisplayName = new Label(label, 1033),
        MinValue = min,
        MaxValue = max,
        RequiredLevel = new AttributeRequiredLevelManagedProperty(required ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None)
    };

    private static PicklistAttributeMetadata SpoChoice(string name, string label, int defaultValue,
        params (string Label, int Value)[] options)
    {
        var optionSet = new OptionSetMetadata { IsGlobal = false };
        foreach (var option in options)
            optionSet.Options.Add(new OptionMetadata(new Label(option.Label, 1033), option.Value));
        return new PicklistAttributeMetadata
        {
            SchemaName = name,
            DisplayName = new Label(label, 1033),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            DefaultFormValue = defaultValue,
            OptionSet = optionSet
        };
    }

    private static IEnumerable<(string Name, string[] Columns, string? Condition)> SpoFileViews()
    {
        var columns = new[] { "fmc_name", "fmc_filename", "fmc_status", "fmc_importstatus", "fmc_filesize", "fmc_processedat", "fmc_errormessage" };
        yield return ("All SPO Files", columns, null);
        yield return ("Archived SPO Files", columns, $"<condition attribute=\"fmc_status\" operator=\"eq\" value=\"{SpoArchiveStatuses.Archived}\" />");
        yield return ("Archive Failed SPO Files", columns, $"<condition attribute=\"fmc_status\" operator=\"eq\" value=\"{SpoArchiveStatuses.Failed}\" />");
        yield return ("Import Failed SPO Files", columns, $"<condition attribute=\"fmc_importstatus\" operator=\"eq\" value=\"{SpoImportStatuses.Failed}\" />");
    }

    private static async Task<EntityMetadata> ReadSpoMetadata(ServiceClient client, string table, EntityFilters filters) =>
        ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = table,
            EntityFilters = filters,
            RetrieveAsIfPublished = true
        })).EntityMetadata;

    private static void RequireStandardOrganizationTable(EntityMetadata metadata)
    {
        if (metadata.TableType != "Standard" || metadata.OwnershipType != OwnershipTypes.OrganizationOwned)
            throw new InvalidOperationException($"{metadata.LogicalName} must be a standard organization-owned table.");
    }

    private static void RequireAttribute<T>(EntityMetadata metadata, string name, Func<T, bool> predicate, string expected)
        where T : AttributeMetadata
    {
        var attribute = metadata.Attributes.SingleOrDefault(a => a.LogicalName == name);
        if (attribute is not T typed || !predicate(typed))
            throw new InvalidOperationException($"{metadata.LogicalName}.{name} must be {expected}.");
    }

    private static void RequireActiveKey(EntityMetadata metadata, string logicalName, string[] columns)
    {
        var key = (metadata.Keys ?? []).SingleOrDefault(k => k.LogicalName == logicalName);
        if (key is null || key.EntityKeyIndexStatus != EntityKeyIndexStatus.Active ||
            !key.KeyAttributes.OrderBy(x => x).SequenceEqual(columns.OrderBy(x => x)))
            throw new InvalidOperationException($"Alternate key {logicalName} is missing, inactive or has different columns.");
    }
}

public static class SpoArchiveStatuses
{
    public const int Received = 789110000;
    public const int Processing = 789110001;
    public const int Archived = 789110002;
    public const int Failed = 789110003;
    public const int Ignored = 789110004;
}

public static class SpoImportStatuses
{
    public const int NotRequested = 789111000;
    public const int Processing = 789111001;
    public const int Imported = 789111002;
    public const int Failed = 789111003;
}
