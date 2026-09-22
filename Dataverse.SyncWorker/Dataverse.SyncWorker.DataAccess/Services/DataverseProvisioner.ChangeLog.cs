using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public sealed partial class DataverseProvisioner
{
    public const string ChangeLogTable = "fmc_changelog";
    public async Task PrintChangeLogStatus()
    {
        var client = connection.Get();
        var who = (WhoAmIResponse)await client.ExecuteAsync(new WhoAmIRequest());
        var tables = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity });
        var log = tables.EntityMetadata.SingleOrDefault(t => t.LogicalName == ChangeLogTable);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { who.OrganizationId, who.UserId,
            tableExists = log is not null, log?.LogicalName, log?.OwnershipType, log?.TableType }));
        var steps = new QueryExpression("sdkmessageprocessingstep") { ColumnSet = new ColumnSet("name", "stage", "mode", "statecode", "filteringattributes") };
        steps.Criteria.AddCondition("name", ConditionOperator.BeginsWith, "BMS: Change Log ");
        foreach (var step in (await client.RetrieveMultipleAsync(steps)).Entities)
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { step.Id, step.Attributes }));
        if (log is not null)
        {
            var metadata = await LogMetadata();
            Console.WriteLine("Log columns: " + string.Join(",", metadata.Attributes.Select(a => a.LogicalName).Where(n => n.StartsWith("fmc_"))));
        }
    }
    private static readonly (string Name, string Label, int Size)[] LogTexts =
    [
        ("fmc_operation", "Operation", 20), ("fmc_tablename", "Table", 100),
        ("fmc_recordid", "Record ID", 36), ("fmc_recordname", "Record Name", 200),
        ("fmc_userid", "Initiating User ID", 36), ("fmc_username", "Changed By", 200),
        ("fmc_runasuserid", "Execution User ID", 36), ("fmc_correlationid", "Correlation ID", 36),
        ("fmc_changedfields", "Changed Fields", 2000)
    ];

    public async Task ProvisionChangeLog()
    {
        var client = connection.Get();
        var solutionQuery = Query("solution", "uniquename", Solution);
        solutionQuery.ColumnSet = new ColumnSet("publisherid");
        var solutions = (await client.RetrieveMultipleAsync(solutionQuery)).Entities;
        if (solutions.Count != 1) throw new InvalidOperationException("Expected existing FMCentralBms solution.");
        var publisher = await client.RetrieveAsync("publisher", solutions[0].GetAttributeValue<EntityReference>("publisherid").Id,
            new ColumnSet("uniquename", "customizationprefix"));
        if (publisher.GetAttributeValue<string>("uniquename") != "FMCentralBmsPublisher" || publisher.GetAttributeValue<string>("customizationprefix") != "fmc")
            throw new InvalidOperationException("Unexpected solution publisher; no writes made.");
        var all = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity });
        var existing = all.EntityMetadata.SingleOrDefault(m => m.LogicalName == ChangeLogTable);
        if (existing is not null && (existing.TableType == "Elastic" || existing.OwnershipType != OwnershipTypes.OrganizationOwned))
            throw new InvalidOperationException("Existing Change Log table has an incompatible contract.");
        if (existing is null)
            await client.ExecuteAsync(new CreateEntityRequest
            {
                SolutionUniqueName = Solution,
                Entity = new EntityMetadata
                {
                    SchemaName = ChangeLogTable, DisplayName = new Label("Change Log", 1033),
                    DisplayCollectionName = new Label("Change Logs", 1033),
                    OwnershipType = OwnershipTypes.OrganizationOwned, TableType = "Standard", IsActivity = false,
                    IsAuditEnabled = new BooleanManagedProperty(false)
                },
                PrimaryAttribute = Text("fmc_name", "Name", 200)
            });
        var metadata = await LogMetadata();
        var columns = LogTexts.Select(x => (AttributeMetadata)Text(x.Name, x.Label, x.Size)).ToList();
        columns.Add(Time("fmc_occurredon", "Changed On"));
        foreach (var (name, label) in new[] { ("fmc_before", "Before (JSON)"), ("fmc_after", "After (JSON)") })
            columns.Add(new MemoAttributeMetadata { SchemaName = name, DisplayName = new Label(label, 1033), MaxLength = 1048576 });
        foreach (var column in columns)
        {
            var present = metadata.Attributes.SingleOrDefault(a => a.LogicalName == column.SchemaName);
            if (present is null)
                await client.ExecuteAsync(new CreateAttributeRequest { EntityName = ChangeLogTable, Attribute = column, SolutionUniqueName = Solution });
            else if (present.GetType() != column.GetType()) throw new InvalidOperationException("Change log column type mismatch: " + column.SchemaName);
        }
        await client.ExecuteAsync(new AddSolutionComponentRequest
        { ComponentId = metadata.MetadataId!.Value, ComponentType = 1, SolutionUniqueName = Solution, AddRequiredComponents = false });
        await ConfigureDefaultView(client, ChangeLogTable, "Active Change Logs",
            ["fmc_occurredon", "fmc_operation", "fmc_tablename", "fmc_recordname", "fmc_username", "fmc_changedfields"]);
        await ConfigureChangeLogForm();
        await ConfigureChangeLogRoles();
        await ConfigureChangeLogApp();
        Console.WriteLine("Change Log table, form, view and app navigation prepared.");
    }

    private async Task<EntityMetadata> LogMetadata() => ((RetrieveEntityResponse)await connection.Get().ExecuteAsync(
        new RetrieveEntityRequest { LogicalName = ChangeLogTable, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true })).EntityMetadata;

    private async Task ConfigureChangeLogForm()
    {
        var client = connection.Get();
        var query = new QueryExpression("systemform") { ColumnSet = new ColumnSet("formxml", "name") };
        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, (await LogMetadata()).ObjectTypeCode!.Value);
        query.Criteria.AddCondition("type", ConditionOperator.Equal, 2);
        query.Criteria.AddCondition("formactivationstate", ConditionOperator.Equal, 1);
        var forms = (await client.RetrieveMultipleAsync(query)).Entities;
        if (forms.Count == 0) throw new InvalidOperationException("Change Log main form not generated yet. Retry after metadata generation.");
        foreach (var form in forms)
        {
            var xml = XElement.Parse(form.GetAttributeValue<string>("formxml"));
            var rows = xml.Descendants("rows").First();
            var fields = LogTexts.Select(x => (x.Name, x.Label, Kind: "text"))
                .Concat(new[] { ("fmc_occurredon", "Changed On", "date"), ("fmc_before", "Before (JSON)", "memo"), ("fmc_after", "After (JSON)", "memo") });
            foreach (var (name, label, kind) in fields)
            {
                if (xml.Descendants("control").Any(c => (string?)c.Attribute("datafieldname") == name)) continue;
                var classId = kind == "memo" ? "{E0DECE4B-6FC8-4A8F-A065-082708572369}" : kind == "date" ? "{5B773807-9FB2-42DB-97C3-7A91EFF8ADFF}" : "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}";
                rows.Add(new XElement("row", new XElement("cell", new XAttribute("id", Guid.NewGuid().ToString("B")),
                    new XElement("labels", new XElement("label", new XAttribute("description", label), new XAttribute("languagecode", "1033"))),
                    new XElement("control", new XAttribute("id", name), new XAttribute("datafieldname", name), new XAttribute("classid", classId), new XAttribute("disabled", "true")))));
            }
            foreach (var control in xml.Descendants("control").Where(c => ((string?)c.Attribute("datafieldname"))?.StartsWith("fmc_") == true))
                control.SetAttributeValue("disabled", "true");
            await client.UpdateAsync(new Entity("systemform", form.Id) { ["formxml"] = xml.ToString(SaveOptions.DisableFormatting) });
        }
    }

    private async Task ConfigureChangeLogRoles()
    {
        var client = connection.Get();
        var metadata = await LogMetadata();
        foreach (var (name, canCreate) in new[] { ("FMC BMS Demo Operator", true), ("FMC BMS Demo Viewer", false), ("FM Central BMS Integration", true) })
        {
            var query = new QueryExpression("role") { ColumnSet = new ColumnSet("name", "parentrootroleid") };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
            query.Criteria.AddCondition("parentroleid", ConditionOperator.Null);
            var roles = (await client.RetrieveMultipleAsync(query)).Entities;
            if (roles.Count != 1) throw new InvalidOperationException("Expected one existing root role: " + name);
            var privileges = metadata.Privileges.Where(p => p.PrivilegeType == PrivilegeType.Read || canCreate && p.PrivilegeType == PrivilegeType.Create)
                .Select(p => new RolePrivilege { PrivilegeId = p.PrivilegeId, Depth = PrivilegeDepth.Global }).ToArray();
            await client.ExecuteAsync(new AddPrivilegesRoleRequest { RoleId = roles[0].Id, Privileges = privileges });
            await client.ExecuteAsync(new AddSolutionComponentRequest { ComponentId = roles[0].Id, ComponentType = 20, SolutionUniqueName = Solution, AddRequiredComponents = false });
        }
    }

    private async Task ConfigureChangeLogApp()
    {
        var client = connection.Get();
        var apps = await client.RetrieveMultipleAsync(Query("appmodule", "uniquename", "fmc_FMCBMSDemo"));
        var sites = Query("sitemap", "sitemapname", "fmc_FMCBMSDemo");
        sites.ColumnSet = new ColumnSet("sitemapxml");
        var maps = (await client.RetrieveMultipleAsync(sites)).Entities;
        if (apps.Entities.Count != 1 || maps.Count != 1) throw new InvalidOperationException("Expected one BMS app/sitemap.");
        var sitemap = XElement.Parse(maps[0].GetAttributeValue<string>("sitemapxml"));
        if (!sitemap.Descendants("SubArea").Any(x => (string?)x.Attribute("Id") == "fmc_changelogs"))
            sitemap.Descendants("Group").First().Add(new XElement("SubArea", new XAttribute("Id", "fmc_changelogs"), new XAttribute("Entity", ChangeLogTable),
                new XElement("Titles", new XElement("Title", new XAttribute("LCID", "1033"), new XAttribute("Title", "Change Logs")))));
        await client.UpdateAsync(new Entity("sitemap", maps[0].Id) { ["sitemapxml"] = sitemap.ToString(SaveOptions.DisableFormatting) });
        var components = new EntityCollection();
        components.Entities.Add(new Entity(ChangeLogTable, (await LogMetadata()).MetadataId!.Value));
        foreach (var (table, idColumn) in new[] { ("savedquery", "savedqueryid"), ("systemform", "formid") })
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false) };
            query.Criteria.AddCondition(table == "savedquery" ? "returnedtypecode" : "objecttypecode", ConditionOperator.Equal, (await LogMetadata()).ObjectTypeCode!.Value);
            foreach (var row in (await client.RetrieveMultipleAsync(query)).Entities) components.Entities.Add(new Entity(table, row.Id));
        }
        await client.ExecuteAsync(new OrganizationRequest("AddAppComponents") { ["AppId"] = apps.Entities[0].Id, ["Components"] = components });
        await client.ExecuteAsync(new PublishXmlRequest { ParameterXml = $"<importexportxml><entities><entity>{ChangeLogTable}</entity></entities><sitemaps><sitemap>{maps[0].Id}</sitemap></sitemaps><appmodules><appmodule>{apps.Entities[0].Id}</appmodule></appmodules></importexportxml>" });
        var validation = await client.ExecuteAsync(new OrganizationRequest("ValidateApp") { ["AppModuleId"] = apps.Entities[0].Id });
        Console.WriteLine("App validation: " + System.Text.Json.JsonSerializer.Serialize(validation.Results));
    }
}
