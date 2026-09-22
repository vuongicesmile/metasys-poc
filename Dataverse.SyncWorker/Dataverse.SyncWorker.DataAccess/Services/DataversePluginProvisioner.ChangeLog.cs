using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

public sealed partial class DataversePluginProvisioner
{
    public static readonly Dictionary<string, string> ChangeLogColumns = new()
    {
        ["fmc_bmsbuilding"] = "fmc_name,fmc_buildingcode,fmc_sourcebuilding,fmc_description,statecode,statuscode",
        ["fmc_bmsequipment"] = "fmc_name,fmc_equipmentcode,fmc_equipmenttype,fmc_buildingid,fmc_description,statecode,statuscode"
    };

    public static void ValidateChangeLogAssembly(string path)
    {
        var assembly = System.Reflection.AssemblyName.GetAssemblyName(Path.GetFullPath(path));
        var token = Convert.ToHexString(assembly.GetPublicKeyToken() ?? []).ToLowerInvariant();
        if (assembly.Name != AssemblyName || token != "e122b5e4dcc2589d" || assembly.Version != new Version(1, 0, 0, 6))
            throw new InvalidOperationException("Change log deployment requires signed FMCentralBms.Plugins 1.0.0.6 with the existing public key token e122b5e4dcc2589d. No writes made.");
    }

    public async Task RegisterChangeLog(string path)
    {
        ValidateChangeLogAssembly(path);
        var client = connection.Get();
        var assembly = await EnsureAssembly(client, path, "1.0.0.6");
        var type = await EnsurePluginType(client, assembly.Id, "FMCentralBms.Plugins.RecordChangeLog", "Record BMS Change Log");
        var solution = await FindSolutionId(client);
        await AddToSolution(client, solution, assembly.Id, PluginAssemblyComponent, "change log assembly", false);
        foreach (var (table, columns) in ChangeLogColumns)
        foreach (var operation in new[] { "Create", "Update", "Delete" })
        {
            var definition = new StepDefinition($"BMS: Change Log {table} {operation}", operation,
                operation == "Update" ? columns : null, table, type.Id, 0, 40,
                "Writes a business change log in the source transaction using selected entity images.");
            var message = await FindMessage(client, operation);
            var filter = await FindMessageFilter(client, message.Id, table);
            // Prepare images while the step is disabled; first deployment cannot run with missing images.
            var step = await EnsureStep(client, definition, message.Id, filter.Id, type.Id, enabled: false);
            if (operation != "Create") await EnsureChangeLogImage(client, step.Id, "Before", 0, columns);
            if (operation != "Delete") await EnsureChangeLogImage(client, step.Id, "After", 1, columns);
            await AddToSolution(client, solution, step.Id, StepComponent, definition.Name, true);
            await client.UpdateAsync(new Entity("sdkmessageprocessingstep", step.Id)
            { ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1) });
        }
    }

    private static async Task EnsureChangeLogImage(Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client,
        Guid step, string alias, int imageType, string columns)
    {
        var query = new QueryExpression("sdkmessageprocessingstepimage") { ColumnSet = new ColumnSet(false) };
        query.Criteria.AddCondition("sdkmessageprocessingstepid", ConditionOperator.Equal, step);
        query.Criteria.AddCondition("entityalias", ConditionOperator.Equal, alias);
        var found = (await client.RetrieveMultipleAsync(query)).Entities;
        if (found.Count > 1) throw new InvalidOperationException("Duplicate change log image.");
        var image = new Entity("sdkmessageprocessingstepimage")
        {
            ["name"] = alias, ["entityalias"] = alias, ["imagetype"] = new OptionSetValue(imageType),
            ["messagepropertyname"] = "Target", ["attributes"] = columns,
            ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", step)
        };
        if (found.Count == 0) await client.CreateAsync(image);
        else { image.Id = found[0].Id; await client.UpdateAsync(image); }
    }
}
