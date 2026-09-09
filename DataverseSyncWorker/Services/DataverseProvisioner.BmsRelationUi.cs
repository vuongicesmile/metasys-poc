using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public sealed partial class DataverseProvisioner
{
    private static readonly string[] BuildingViewColumns = ["fmc_buildingcode", "fmc_name", "fmc_sourcebuilding", "statecode"];
    private static readonly string[] EquipmentViewColumns = ["fmc_equipmentcode", "fmc_name", "fmc_equipmenttype", "fmc_buildingid", "statecode"];
    private static readonly string[] PointViewColumns = ["fmc_objectid", "fmc_name", "fmc_equipmentid", "fmc_building", "fmc_currentvalue", "fmc_unit", "fmc_lastreadingtime"];
    private static readonly (string Table, string Name, string[] Columns, bool Unassigned)[] RelationViews =
    [
        (BuildingTable, "Active BMS Buildings", BuildingViewColumns, false),
        (EquipmentTable, "Active BMS Equipment", EquipmentViewColumns, false),
        (PointTable, "Active BMS Points", PointViewColumns, false),
        (PointTable, "Unassigned BMS Points", PointViewColumns, true)
    ];

    private async Task ConfigureBmsRelationUi()
    {
        var views = new Dictionary<string, Guid>();
        foreach (var view in RelationViews)
            views[view.Name] = await EnsureRelationView(view.Table, view.Name, view.Columns, view.Unassigned);
        foreach (var table in RelationTables)
        {
            var forms = await RelationMainForms(table);
            foreach (var form in forms)
            {
                var xml = BuildRelationForm(form.GetAttributeValue<string>("formxml"), table, form.Id,
                    table == BuildingTable ? views["Active BMS Equipment"] : table == EquipmentTable ? views["Active BMS Points"] : Guid.Empty);
                if (XNode.DeepEquals(XElement.Parse(form.GetAttributeValue<string>("formxml")), XElement.Parse(xml)))
                    Console.WriteLine($"Form already configured: {table}/{form.Id}.");
                else
                    await connection.Get().UpdateAsync(new Entity("systemform", form.Id) { ["formxml"] = xml });
                await AddRelationComponent(form.Id, 60);
            }
        }
    }

    private async Task<Guid> EnsureRelationView(string table, string name, string[] columns, bool unassigned)
    {
        var metadata = await RelationMetadata(table);
        var matches = await FindRelationView(table, name);
        if (matches.Count > 1) throw new InvalidOperationException($"Ambiguous public view: {table}/{name}.");
        var entity = new XElement("entity", new XAttribute("name", table),
            columns.Prepend(table + "id").Select(c => new XElement("attribute", new XAttribute("name", c))),
            new XElement("order", new XAttribute("attribute", "fmc_name"), new XAttribute("descending", "false")));
        var filter = new XElement("filter", new XAttribute("type", "and"));
        // Include inactive points in the unassigned view so no unclassified record is hidden.
        if (!unassigned) filter.Add(new XElement("condition", new XAttribute("attribute", "statecode"), new XAttribute("operator", "eq"), new XAttribute("value", "0")));
        if (unassigned) filter.Add(new XElement("condition", new XAttribute("attribute", "fmc_equipmentid"), new XAttribute("operator", "null")));
        entity.Add(filter);
        var fetch = new XElement("fetch", new XAttribute("version", "1.0"), new XAttribute("mapping", "logical"), entity);
        var layout = new XElement("grid", new XAttribute("name", "resultset"), new XAttribute("object", metadata.ObjectTypeCode!.Value),
            new XAttribute("jump", "fmc_name"), new XAttribute("select", "1"), new XAttribute("icon", "1"), new XAttribute("preview", "1"),
            new XElement("row", new XAttribute("name", "result"), new XAttribute("id", table + "id"),
                columns.Select(c => new XElement("cell", new XAttribute("name", c), new XAttribute("width", "180")))));
        var values = new Entity("savedquery")
        {
            ["name"] = name, ["returnedtypecode"] = metadata.ObjectTypeCode.Value, ["querytype"] = 0,
            ["fetchxml"] = fetch.ToString(SaveOptions.DisableFormatting),
            ["layoutxml"] = layout.ToString(SaveOptions.DisableFormatting)
        };
        Guid id;
        if (matches.Count == 0) id = await connection.Get().CreateAsync(values);
        else
        {
            id = matches[0].Id;
            if (!XmlEqual(matches[0].GetAttributeValue<string>("fetchxml"), (string)values["fetchxml"]) ||
                !XmlEqual(matches[0].GetAttributeValue<string>("layoutxml"), (string)values["layoutxml"]))
                await connection.Get().UpdateAsync(new Entity("savedquery", id)
                    { ["fetchxml"] = values["fetchxml"], ["layoutxml"] = values["layoutxml"] });
        }
        await AddRelationComponent(id, 26);
        Console.WriteLine($"View ready: {table}/{name} ({id}).");
        return id;
    }

    private static bool XmlEqual(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left) && XNode.DeepEquals(XElement.Parse(left), XElement.Parse(right));

    private async Task<List<Entity>> FindRelationView(string table, string name)
    {
        var metadata = await RelationMetadata(table);
        var q = new QueryExpression("savedquery") { ColumnSet = new ColumnSet("name", "fetchxml", "layoutxml"), TopCount = 2 };
        q.Criteria.AddCondition("name", ConditionOperator.Equal, name);
        q.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, metadata.ObjectTypeCode!.Value);
        q.Criteria.AddCondition("querytype", ConditionOperator.Equal, 0);
        return (await connection.Get().RetrieveMultipleAsync(q)).Entities.ToList();
    }

    private async Task<List<Entity>> RelationMainForms(string table)
    {
        var metadata = await RelationMetadata(table);
        var q = new QueryExpression("systemform") { ColumnSet = new ColumnSet("name", "formxml") };
        q.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, metadata.ObjectTypeCode!.Value);
        q.Criteria.AddCondition("type", ConditionOperator.Equal, 2);
        q.Criteria.AddCondition("formactivationstate", ConditionOperator.Equal, 1);
        var forms = (await connection.Get().RetrieveMultipleAsync(q)).Entities.ToList();
        if (forms.Count == 0) throw new InvalidOperationException($"No active main form available for {table}; retry after metadata generation.");
        return forms;
    }

    internal static string BuildRelationForm(string original, string table, Guid formId, Guid childView)
    {
        var form = XElement.Parse(original);
        var tabs = form.Element("tabs") ?? throw new InvalidOperationException("Main form has no tabs element.");
        var existingTab = tabs.Elements("tab").SingleOrDefault(t => (string?)t.Attribute("name") == "fmc_bmsrelations");
        // Preserve existing controls/tabs; append only this feature's missing controls.
        var rows = existingTab?.Descendants("rows").FirstOrDefault();
        string Id(string key) => ReadingMapper.StableGuid($"bms-relation-form|{formId:D}|{key}").ToString("B");
        static XElement Labels(string text) => new("labels", new XElement("label", new XAttribute("description", text), new XAttribute("languagecode", "1033")));
        if (existingTab is null)
        {
            rows = new XElement("rows");
            existingTab = new XElement("tab", new XAttribute("name", "fmc_bmsrelations"), new XAttribute("id", Id("tab")),
                new XAttribute("showlabel", "true"), new XAttribute("expanded", "true"), Labels("BMS Details and Relationships"),
                new XElement("columns", new XElement("column", new XAttribute("width", "100%"),
                    new XElement("sections", new XElement("section", new XAttribute("name", "fmc_bmsrelation_details"),
                        new XAttribute("id", Id("section")), new XAttribute("showlabel", "false"), new XAttribute("showbar", "false"),
                        new XAttribute("columns", "1"), Labels("BMS Details"), rows)))));
            tabs.Add(existingTab);
        }
        if (rows is null) throw new InvalidOperationException("Existing BMS relationships tab has no rows; inspect before editing.");
        var fields = table switch
        {
            BuildingTable => new[] { ("fmc_buildingcode", "Building Code", "text"), ("fmc_sourcebuilding", "Source Building", "text"), ("fmc_description", "Description", "memo") },
            EquipmentTable => new[] { ("fmc_equipmentcode", "Equipment Code", "text"), ("fmc_equipmenttype", "Equipment Type", "choice"), ("fmc_buildingid", "Building", "lookup"), ("fmc_description", "Description", "memo") },
            PointTable => new[] { ("fmc_objectid", "Object ID", "text"), ("fmc_equipmentid", "Equipment", "lookup"), ("fmc_building", "Source Building", "text"), ("fmc_unit", "Unit", "text") },
            _ => throw new InvalidOperationException("Unexpected BMS form table.")
        };
        foreach (var (name, label, kind) in fields)
        {
            if (form.Descendants("control").Any(c => (string?)c.Attribute("datafieldname") == name)) continue;
            var classId = kind switch
            {
                "lookup" => "{270BD3DB-D9AF-4782-9025-509E298DEC0A}",
                "choice" => "{3EF39988-22BB-4F0B-BBBE-64B5A3748AEE}",
                "memo" => "{E0DECE4B-6FC8-4A8F-A065-082708572369}",
                _ => "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}"
            };
            rows.Add(new XElement("row", new XElement("cell", new XAttribute("id", Id(name)), Labels(label),
                new XElement("control", new XAttribute("id", name), new XAttribute("classid", classId), new XAttribute("datafieldname", name)))));
        }
        if (table != PointTable && !form.Descendants("control").Any(c => (string?)c.Attribute("id") == "fmc_bmsrelatedrecords"))
        {
            if (childView == Guid.Empty) throw new InvalidOperationException("A related-records subgrid requires an existing public view.");
            var child = table == BuildingTable ? EquipmentTable : PointTable;
            rows.Add(new XElement("row", new XElement("cell", new XAttribute("id", Id("subgrid")),
                new XAttribute("showlabel", "true"), Labels(table == BuildingTable ? "BMS Equipment" : "BMS Points"),
                new XElement("control", new XAttribute("id", "fmc_bmsrelatedrecords"),
                    new XAttribute("classid", "{E7A81278-8635-4D9E-8D4D-59480B391C5B}"),
                    new XElement("parameters", new XElement("TargetEntityType", child),
                        new XElement("ViewId", childView.ToString("B")), new XElement("IsUserView", "false"),
                        new XElement("RelationshipName", table == BuildingTable ? BuildingRelationship : EquipmentRelationship),
                        new XElement("RecordsPerPage", "10"), new XElement("EnableQuickFind", "false"))))));
        }
        return form.ToString(SaveOptions.DisableFormatting);
    }

    public async Task VerifyBmsRelationUi()
    {
        foreach (var view in RelationViews)
        {
            var matches = await FindRelationView(view.Table, view.Name);
            if (matches.Count != 1) throw new InvalidOperationException($"Missing/ambiguous BMS view: {view.Name}.");
            var fetch = XElement.Parse(matches[0].GetAttributeValue<string>("fetchxml"));
            var layout = XElement.Parse(matches[0].GetAttributeValue<string>("layoutxml"));
            if (view.Columns.Any(c => !fetch.Descendants("attribute").Any(a => (string?)a.Attribute("name") == c) ||
                                     !layout.Descendants("cell").Any(a => (string?)a.Attribute("name") == c)))
                throw new InvalidOperationException($"View columns are incomplete: {view.Name}.");
            if (view.Unassigned && !fetch.Descendants("condition").Any(c =>
                    (string?)c.Attribute("attribute") == "fmc_equipmentid" && (string?)c.Attribute("operator") == "null"))
                throw new InvalidOperationException("Unassigned BMS Points filter is missing.");
        }
        foreach (var table in RelationTables)
        foreach (var form in await RelationMainForms(table))
        {
            var xml = XElement.Parse(form.GetAttributeValue<string>("formxml"));
            var required = table == BuildingTable ? "fmc_buildingcode" : table == EquipmentTable ? "fmc_buildingid" : "fmc_equipmentid";
            if (!xml.Descendants("control").Any(c => (string?)c.Attribute("datafieldname") == required))
                throw new InvalidOperationException($"Form lookup/code missing: {table}/{form.Id}.");
            if (table != PointTable)
            {
                var relationship = table == BuildingTable ? BuildingRelationship : EquipmentRelationship;
                if (!xml.Descendants("RelationshipName").Any(r => r.Value == relationship))
                    throw new InvalidOperationException($"Related-records subgrid missing: {table}/{form.Id}.");
            }
        }
        Console.WriteLine("Verified public views and main-form controls/subgrids. Table solution membership covers their subcomponents.");
    }
}
