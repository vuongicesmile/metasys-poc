using System.Reflection;
using DataverseSyncWorker.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

/// <summary>
/// Registers the BMS validation assembly and its two solution-aware steps.
/// This is an explicit deployment command; ordinary worker startup never calls it.
/// </summary>
public sealed class DataversePluginProvisioner(DataverseConnection connection, SyncOptions options)
{
    private const string AssemblyName = "FMCentralBms.Plugins";
    private const string PluginTypeName = "FMCentralBms.Plugins.RequireEquipmentBuilding";
    private const string Table = "fmc_bmsequipment";
    private const string Solution = DataverseProvisioner.Solution;

    // Dataverse solution component type codes for plug-in type, assembly and step.
    private const int PluginAssemblyComponent = 91;
    private const int StepComponent = 92;

    public async Task Register(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Plug-in assembly was not found.", fullPath);

        var localVersion = System.Reflection.AssemblyName.GetAssemblyName(fullPath).Version?.ToString()
            ?? throw new InvalidOperationException("Plug-in assembly has no version.");
        var client = connection.Get();
        var assembly = await EnsureAssembly(client, fullPath, localVersion);
        var pluginType = await EnsurePluginType(client, assembly.Id);
        var solutionId = await FindSolutionId(client);

        await AddToSolution(client, solutionId, assembly.Id, PluginAssemblyComponent,
            $"assembly {AssemblyName}", addRequiredComponents: false);

        foreach (var definition in new[]
        {
            new StepDefinition(
                "BMS: Require Building on Equipment Create", "Create", null),
            new StepDefinition(
                "BMS: Require Building on Equipment Update", "Update", "fmc_buildingid")
        })
        {
            var message = await FindMessage(client, definition.Message);
            var filter = await FindMessageFilter(client, message.Id, Table);
            var step = await EnsureStep(client, definition, message.Id, filter.Id, pluginType.Id);
            await AddToSolution(client, solutionId, step.Id, StepComponent,
                $"step {definition.Name}", addRequiredComponents: true);
            Console.WriteLine($"Ready: {definition.Name} ({step.Id}).");
        }

        Console.WriteLine($"Plug-in registration complete in {options.Url.TrimEnd('/')}");
    }

    private static async Task<Entity> EnsureAssembly(ServiceClient client, string path, string localVersion)
    {
        var query = new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet("name", "version", "isolationmode", "sourcetype")
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, AssemblyName);
        var matches = (await client.RetrieveMultipleAsync(query)).Entities;
        if (matches.Count > 1)
            throw new InvalidOperationException($"Found multiple Dataverse assemblies named '{AssemblyName}'.");

        var content = Convert.ToBase64String(await File.ReadAllBytesAsync(path));
        if (matches.Count == 0)
        {
            var create = new Entity("pluginassembly")
            {
                ["name"] = AssemblyName,
                ["content"] = content,
                ["isolationmode"] = new OptionSetValue(2),
                ["sourcetype"] = new OptionSetValue(0),
                ["description"] = "FMCentralBms Equipment-to-Building validation plug-in."
            };
            var id = await client.CreateAsync(create);
            Console.WriteLine($"Created plug-in assembly {id} (version {localVersion}).");
            return new Entity("pluginassembly", id);
        }

        var existing = matches[0];
        var existingVersion = existing.GetAttributeValue<string>("version");
        if (!string.Equals(existingVersion, localVersion, StringComparison.OrdinalIgnoreCase))
        {
            await client.UpdateAsync(new Entity("pluginassembly", existing.Id)
            {
                ["content"] = content,
                ["isolationmode"] = new OptionSetValue(2),
                ["sourcetype"] = new OptionSetValue(0),
                ["description"] = "FMCentralBms Equipment-to-Building validation plug-in."
            });
            Console.WriteLine($"Updated plug-in assembly {existing.Id} to version {localVersion}.");
        }
        else
        {
            Console.WriteLine($"Plug-in assembly already exists at version {existingVersion}; keeping its registered bytes.");
        }

        return existing;
    }

    private static async Task<Entity> EnsurePluginType(ServiceClient client, Guid assemblyId)
    {
        var query = new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet("name", "typename", "pluginassemblyid")
        };
        query.Criteria.AddCondition("pluginassemblyid", ConditionOperator.Equal, assemblyId);
        query.Criteria.AddCondition("typename", ConditionOperator.Equal, PluginTypeName);
        var matches = (await client.RetrieveMultipleAsync(query)).Entities;
        if (matches.Count > 1)
            throw new InvalidOperationException($"Found multiple Dataverse plug-in types named '{PluginTypeName}'.");
        if (matches.Count == 1)
        {
            Console.WriteLine($"Plug-in type already exists ({matches[0].Id}).");
            return matches[0];
        }

        var create = new Entity("plugintype")
        {
            ["name"] = PluginTypeName,
            ["friendlyname"] = "Require Equipment Building",
            ["typename"] = PluginTypeName,
            ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId)
        };
        var id = await client.CreateAsync(create);
        Console.WriteLine($"Created plug-in type {id}.");
        return new Entity("plugintype", id);
    }

    private static async Task<Entity> EnsureStep(
        ServiceClient client, StepDefinition definition, Guid messageId, Guid filterId, Guid pluginTypeId)
    {
        var query = new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet(
                "name", "mode", "stage", "rank", "supporteddeployment",
                "filteringattributes", "statecode", "statuscode", "plugintypeid",
                "sdkmessageid", "sdkmessagefilterid")
        };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, definition.Name);
        var matches = (await client.RetrieveMultipleAsync(query)).Entities;
        if (matches.Count > 1)
            throw new InvalidOperationException($"Found multiple Dataverse steps named '{definition.Name}'.");

        var values = new Entity("sdkmessageprocessingstep")
        {
            ["name"] = definition.Name,
            ["description"] = "FMCentralBms plan: validate Equipment Building lookup.",
            ["mode"] = new OptionSetValue(0),
            ["stage"] = new OptionSetValue(10),
            ["rank"] = 10,
            ["supporteddeployment"] = new OptionSetValue(0),
            ["filteringattributes"] = definition.FilteringAttributes,
            ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
            ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
            ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId),
            ["statecode"] = new OptionSetValue(0),
            ["statuscode"] = new OptionSetValue(1)
        };

        if (matches.Count == 0)
        {
            var id = await client.CreateAsync(values);
            Console.WriteLine($"Created step {definition.Name} ({id}).");
            return new Entity("sdkmessageprocessingstep", id);
        }

        var existing = matches[0];
        values.Id = existing.Id;
        await client.UpdateAsync(values);
        Console.WriteLine($"Updated step {definition.Name} ({existing.Id}).");
        return existing;
    }

    private static async Task<Entity> FindMessage(ServiceClient client, string name)
    {
        var query = new QueryExpression("sdkmessage") { ColumnSet = new ColumnSet(false) };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
        var matches = (await client.RetrieveMultipleAsync(query)).Entities;
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"Expected one sdkmessage named '{name}', found {matches.Count}.");
    }

    private static async Task<Entity> FindMessageFilter(ServiceClient client, Guid messageId, string table)
    {
        var query = new QueryExpression("sdkmessagefilter")
        {
            ColumnSet = new ColumnSet("primaryobjecttypecode")
        };
        query.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
        query.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, table);
        var matches = (await client.RetrieveMultipleAsync(query)).Entities;
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one {table} sdkmessagefilter for message {messageId}, found {matches.Count}.");
    }

    private static async Task<Guid> FindSolutionId(ServiceClient client)
    {
        var query = new QueryExpression("solution") { ColumnSet = new ColumnSet(false) };
        query.Criteria.AddCondition("uniquename", ConditionOperator.Equal, Solution);
        var matches = (await client.RetrieveMultipleAsync(query)).Entities;
        return matches.Count == 1
            ? matches[0].Id
            : throw new InvalidOperationException($"Expected one solution '{Solution}', found {matches.Count}.");
    }

    private static async Task AddToSolution(
        ServiceClient client, Guid solutionId, Guid componentId, int componentType, string description,
        bool addRequiredComponents)
    {
        var query = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet(false) };
        query.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);
        query.Criteria.AddCondition("objectid", ConditionOperator.Equal, componentId);
        query.Criteria.AddCondition("componenttype", ConditionOperator.Equal, componentType);
        if ((await client.RetrieveMultipleAsync(query)).Entities.Count != 0)
        {
            Console.WriteLine($"Solution already contains {description}.");
            return;
        }

        await client.ExecuteAsync(new AddSolutionComponentRequest
        {
            ComponentId = componentId,
            ComponentType = componentType,
            SolutionUniqueName = Solution,
            AddRequiredComponents = addRequiredComponents
        });
        Console.WriteLine($"Added {description} to solution {Solution}.");
    }

    private sealed record StepDefinition(string Name, string Message, string? FilteringAttributes);
}
