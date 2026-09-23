using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

/// <summary>Explicit provisioning and verification for the SPO change debounce queue.</summary>
public sealed partial class DataverseProvisioner
{
    public const string SpoChangeRequestTable = "fmc_spochangerequest";
    public const string SpoChangeActiveKey = "fmc_spochangerequest_activekey";
    public const string SpoChangeCorrelationKey = "fmc_spochangerequest_correlationkey";

    public async Task PrintSpoChangeStatus()
    {
        var client = connection.Get();
        var all = (RetrieveAllEntitiesResponse)await client.ExecuteAsync(new RetrieveAllEntitiesRequest
        {
            EntityFilters = EntityFilters.Entity
        });
        var metadata = all.EntityMetadata.SingleOrDefault(x => x.LogicalName == SpoChangeRequestTable);
        var detailedMetadata = metadata is null
            ? null
            : await ReadSpoMetadata(client, SpoChangeRequestTable, EntityFilters.All);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Environment = options.Url,
            Solution,
            Exists = metadata is not null,
            Table = detailedMetadata is null ? null : new { detailedMetadata.TableType, Ownership = detailedMetadata.OwnershipType.ToString() },
            Keys = detailedMetadata?.Keys?.Select(x => new { x.LogicalName, Status = x.EntityKeyIndexStatus.ToString(), x.KeyAttributes }).ToArray()
        }));
    }

    public async Task ProvisionSpoChanges()
    {
        var client = connection.Get();
        await RequireSpoSolution(client);
        await EnsureSpoTable(client, SpoChangeRequestTable, "SPO Change Request", "SPO Change Requests", SpoChangeColumns());
        await EnsureSpoKey(client, SpoChangeRequestTable, SpoChangeActiveKey, "Active SPO Change", ["fmc_activekey"]);
        await EnsureSpoKey(client, SpoChangeRequestTable, SpoChangeCorrelationKey, "SPO Change Dispatch Correlation", ["fmc_correlationkey"]);
        await PublishSpoChangeTable(client);
        await WaitForSpoChangeKeys(client);

        foreach (var view in SpoChangeViews())
            await EnsureSpoView(client, SpoChangeRequestTable, view.Name, view.Columns, view.Condition);
        await EnsureSpoIngestionRole(client);
        await EnsureSpoChangeAppRoles(client);
        await AddSpoChangeToDemoApp(client);
        await PublishSpoChangeTable(client, includeApp: true);
        await VerifySpoChanges();
        Console.WriteLine("SPO change request queue, keys, views, runtime role and Demo app navigation are ready.");
    }

    public async Task VerifySpoChanges()
    {
        var client = connection.Get();
        await RequireSpoSolution(client);
        var metadata = await ReadSpoMetadata(client, SpoChangeRequestTable, EntityFilters.All);
        RequireStandardOrganizationTable(metadata);
        RequireAttribute<StringAttributeMetadata>(metadata, "fmc_sourcekey", x => x.MaxLength == 150 && x.RequiredLevel.Value == AttributeRequiredLevel.ApplicationRequired, "Text(150), ApplicationRequired");
        RequireAttribute<StringAttributeMetadata>(metadata, "fmc_expectedetag", x => x.MaxLength == 200 && x.RequiredLevel.Value == AttributeRequiredLevel.ApplicationRequired, "Text(200), ApplicationRequired");
        RequireAttribute<PicklistAttributeMetadata>(metadata, "fmc_status", x => x.OptionSet.Options.Any(o => o.Value == SpoChangeStatuses.Pending) && x.OptionSet.Options.Any(o => o.Value == SpoChangeStatuses.Imported), "the SPO change status options");
        RequireActiveKey(metadata, SpoChangeActiveKey, ["fmc_activekey"]);
        RequireActiveKey(metadata, SpoChangeCorrelationKey, ["fmc_correlationkey"]);
        foreach (var view in SpoChangeViews())
            if ((await FindSpoViews(client, view.Name, SpoChangeRequestTable)).Count != 1)
                throw new InvalidOperationException($"Expected exactly one public view named '{view.Name}'.");

        var app = await FindSingle(client, "appmodule", "uniquename", DemoAppUniqueName, new ColumnSet("appmoduleid"));
        var site = await FindSingle(client, "sitemap", "sitemapname", DemoSiteMapName, new ColumnSet("sitemapid", "sitemapxml"));
        if (!XElement.Parse(site.GetAttributeValue<string>("sitemapxml")).Descendants("SubArea")
                .Any(x => (string?)x.Attribute("Entity") == SpoChangeRequestTable))
            throw new InvalidOperationException("FMC BMS Demo sitemap does not contain the SPO Change Requests page.");
        await RequireSolutionComponent(client, app.Id, 80);
        await RequireSolutionComponent(client, site.Id, 62);
        await RequireSolutionComponent(client, metadata.MetadataId!.Value, 1);
        Console.WriteLine("Verified SPO change queue metadata, keys, views, sitemap and solution membership.");
    }

    private async Task WaitForSpoChangeKeys(ServiceClient client)
    {
        for (var attempt = 0; attempt <= 30; attempt++)
        {
            var keys = (await ReadSpoMetadata(client, SpoChangeRequestTable, EntityFilters.All)).Keys!
                .Where(x => x.LogicalName is SpoChangeActiveKey or SpoChangeCorrelationKey).ToArray();
            if (keys.Length != 2 || keys.Any(x => x.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed))
                throw new InvalidOperationException("An SPO change alternate-key index is missing or failed. Inspect the async job before retrying.");
            if (keys.All(x => x.EntityKeyIndexStatus == EntityKeyIndexStatus.Active)) return;
            if (attempt == 30) throw new TimeoutException("SPO change alternate-key indexes are not Active after 150 seconds.");
            Console.WriteLine("Waiting for SPO change alternate-key indexes...");
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task AddSpoChangeToDemoApp(ServiceClient client)
    {
        var app = await FindSingle(client, "appmodule", "uniquename", DemoAppUniqueName, new ColumnSet("appmoduleid"));
        var site = await FindSingle(client, "sitemap", "sitemapname", DemoSiteMapName, new ColumnSet("sitemapid", "sitemapxml"));
        var xml = XElement.Parse(site.GetAttributeValue<string>("sitemapxml"));
        var matches = xml.Descendants("SubArea").Where(x => (string?)x.Attribute("Entity") == SpoChangeRequestTable).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("FMC BMS Demo contains duplicate SPO Change Requests sitemap entries.");
        if (matches.Length == 0)
        {
            var group = xml.Descendants("Group").FirstOrDefault(x => (string?)x.Attribute("Id") == "fmc_catalog")
                ?? xml.Descendants("Group").FirstOrDefault(x => x.Descendants("Title").Any(t => string.Equals((string?)t.Attribute("Title"), "SharePoint", StringComparison.OrdinalIgnoreCase)))
                ?? throw new InvalidOperationException("FMC BMS Demo SharePoint group is missing; refusing to choose another sitemap group.");
            group.Add(new XElement("SubArea", new XAttribute("Id", "fmc_spochangerequests"), new XAttribute("Entity", SpoChangeRequestTable),
                new XElement("Titles", new XElement("Title", new XAttribute("LCID", "1033"), new XAttribute("Title", "SPO Change Requests")))));
            await client.UpdateAsync(new Entity("sitemap", site.Id) { ["sitemapxml"] = xml.ToString(SaveOptions.DisableFormatting) });
        }

        await AddSpoSolutionComponent(client, site.Id, 62, false);
        await AddSpoSolutionComponent(client, app.Id, 80, false);
        var metadata = await ReadSpoMetadata(client, SpoChangeRequestTable, EntityFilters.Entity);
        var components = new EntityReferenceCollection { new(SpoChangeRequestTable, metadata.MetadataId!.Value) };
        foreach (var view in SpoChangeViews())
        foreach (var item in await FindSpoViews(client, view.Name, SpoChangeRequestTable))
            components.Add(item.ToEntityReference());
        await client.ExecuteAsync(new OrganizationRequest("AddAppComponents")
        {
            ["AppId"] = app.Id,
            ["Components"] = components
        });
        Console.WriteLine("SPO Change Requests navigation added to FMC BMS Demo.");
    }

    private async Task EnsureSpoChangeAppRoles(ServiceClient client)
    {
        var metadata = await ReadSpoMetadata(client, SpoChangeRequestTable, EntityFilters.Privileges);
        foreach (var (name, allowDispatch) in new[] { ("FMC BMS Demo Operator", true), ("FMC BMS Demo Viewer", false) })
        {
            var query = new QueryExpression("role") { ColumnSet = new ColumnSet("roleid", "name") };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
            query.Criteria.AddCondition("parentroleid", ConditionOperator.Null);
            var roles = (await client.RetrieveMultipleAsync(query)).Entities;
            if (roles.Count != 1) throw new InvalidOperationException($"Expected one existing root role: {name}.");
            var privileges = metadata.Privileges
                .Where(x => x.PrivilegeType == PrivilegeType.Read || allowDispatch && x.PrivilegeType == PrivilegeType.Write)
                .Select(x => new RolePrivilege { PrivilegeId = x.PrivilegeId, Depth = PrivilegeDepth.Global }).ToArray();
            await client.ExecuteAsync(new AddPrivilegesRoleRequest { RoleId = roles[0].Id, Privileges = privileges });
            await AddSpoSolutionComponent(client, roles[0].Id, 20, false);
        }
        Console.WriteLine("SPO change read/dispatch privileges are ready for Demo Operator and Viewer roles; no role assignment was changed.");
    }

    private async Task PublishSpoChangeTable(ServiceClient client, bool includeApp = false)
    {
        var extra = "";
        if (includeApp)
        {
            var app = await FindSingle(client, "appmodule", "uniquename", DemoAppUniqueName, new ColumnSet("appmoduleid"));
            var site = await FindSingle(client, "sitemap", "sitemapname", DemoSiteMapName, new ColumnSet("sitemapid"));
            extra = $"<sitemaps><sitemap>{site.Id}</sitemap></sitemaps><appmodules><appmodule>{app.Id}</appmodule></appmodules>";
        }
        await client.ExecuteAsync(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{SpoChangeRequestTable}</entity></entities>{extra}</importexportxml>"
        });
    }

    private static IEnumerable<AttributeMetadata> SpoChangeColumns()
    {
        yield return SpoText("fmc_sourcekey", "Source Key", 150, true);
        yield return SpoText("fmc_activekey", "Active Source Key", 150);
        yield return SpoText("fmc_libraryid", "SharePoint Library ID", 200, true);
        yield return SpoText("fmc_itemid", "SharePoint Item ID", 100, true);
        yield return SpoText("fmc_sharepointidentifier", "SharePoint Identifier", 4000, true);
        yield return SpoText("fmc_sharepointpath", "SharePoint Path", 4000, true);
        yield return SpoText("fmc_filename", "File Name", 255, true);
        yield return SpoText("fmc_expectedetag", "Expected ETag", 200, true);
        yield return Time("fmc_detectedat", "Detected At");
        yield return Time("fmc_dueat", "Auto Sync Due At");
        yield return SpoChoice("fmc_status", "Change Status", SpoChangeStatuses.Pending,
            ("Pending", SpoChangeStatuses.Pending), ("Dispatching", SpoChangeStatuses.Dispatching),
            ("Dispatched", SpoChangeStatuses.Dispatched), ("Superseded", SpoChangeStatuses.Superseded),
            ("Failed", SpoChangeStatuses.Failed), ("Imported", SpoChangeStatuses.Imported));
        yield return SpoText("fmc_dispatchsource", "Dispatch Source", 30);
        yield return Time("fmc_dispatchedat", "Dispatched At");
        yield return SpoText("fmc_correlationkey", "Dispatch Correlation Key", 100);
        yield return SpoText("fmc_flowrunid", "Flow Run ID", 100);
        yield return new MemoAttributeMetadata { SchemaName = "fmc_errormessage", DisplayName = new Label("Error Message", 1033), MaxLength = 4000 };
    }

    private static IEnumerable<(string Name, string[] Columns, string? Condition)> SpoChangeViews()
    {
        var columns = new[] { "fmc_name", "fmc_status", "fmc_expectedetag", "fmc_detectedat", "fmc_dueat", "fmc_dispatchedat", "fmc_dispatchsource", "fmc_errormessage" };
        yield return ("All SPO Change Requests", columns, null);
        yield return ("Pending SPO Change Requests", columns, $"<condition attribute=\"fmc_status\" operator=\"eq\" value=\"{SpoChangeStatuses.Pending}\" />");
        yield return ("Failed SPO Change Requests", columns, $"<condition attribute=\"fmc_status\" operator=\"eq\" value=\"{SpoChangeStatuses.Failed}\" />");
    }
}

public static class SpoChangeStatuses
{
    public const int Pending = 789112000;
    public const int Dispatching = 789112001;
    public const int Dispatched = 789112002;
    public const int Superseded = 789112003;
    public const int Failed = 789112004;
    public const int Imported = 789112005;
}
