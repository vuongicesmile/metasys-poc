using System.Text.Json;
using DataverseSyncWorker.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DataverseSyncWorker.Services;

/// <summary>Creates the solution-aware manual cloud flow and its ALM dependencies.</summary>
public sealed class PowerAutomateProvisioner(DataverseConnection connection, SyncOptions options)
{
    private const string ConnectorId = "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps";
    private const string FlowName = "FMC - Request SQL to Dataverse Sync";
    private const string PipelineVariable = "fmc_SyncPipeline";

    public async Task Run()
    {
        var client = connection.Get();
        _ = await EnsureConnectionReference(client);
        var environmentVariableId = await EnsurePipelineVariable(client);

        var requestMetadata = ((RetrieveEntityResponse)await client.ExecuteAsync(new RetrieveEntityRequest
        {
            LogicalName = "fmc_syncrequest", EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = true
        })).EntityMetadata;
        var entitySet = requestMetadata.EntitySetName ?? throw new InvalidOperationException("Missing fmc_syncrequest entity set name.");
        var clientData = BuildClientData(entitySet);

        var query = new QueryExpression("workflow") { ColumnSet = new ColumnSet("workflowid", "statecode") };
        query.Criteria.AddCondition("name", ConditionOperator.Equal, FlowName);
        query.Criteria.AddCondition("category", ConditionOperator.Equal, 5);
        var matches = await client.RetrieveMultipleAsync(query);
        if (matches.Entities.Count > 1)
            throw new InvalidOperationException($"More than one cloud flow is named '{FlowName}'.");

        Guid workflowId;
        if (matches.Entities.Count == 0)
        {
            workflowId = await client.CreateAsync(new Entity("workflow")
            {
                ["category"] = new OptionSetValue(5),
                ["name"] = FlowName,
                ["type"] = new OptionSetValue(1),
                ["description"] = "Queues one SQL-to-Dataverse synchronization request. DataverseSyncWorker processes the request asynchronously.",
                ["primaryentity"] = "none",
                ["clientdata"] = clientData
            });
        }
        else
        {
            workflowId = matches.Entities.Single().Id;
            await client.UpdateAsync(new Entity("workflow", workflowId)
            {
                ["description"] = "Queues one SQL-to-Dataverse synchronization request. DataverseSyncWorker processes the request asynchronously.",
                ["clientdata"] = clientData
            });
        }

        // Connection references are flow dependencies rather than connector root components.
        // AddRequiredComponents on the workflow carries the reference into the solution.
        await client.ExecuteAsync(new AddSolutionComponentRequest
        {
            ComponentId = environmentVariableId, ComponentType = 380,
            SolutionUniqueName = DataverseProvisioner.Solution, AddRequiredComponents = false
        });
        await client.ExecuteAsync(new AddSolutionComponentRequest
        {
            ComponentId = workflowId, ComponentType = 29,
            SolutionUniqueName = DataverseProvisioner.Solution, AddRequiredComponents = true
        });

        await client.UpdateAsync(new Entity("workflow", workflowId)
        {
            ["statecode"] = new OptionSetValue(1)
        });
        Console.WriteLine($"Power Automate flow active: {FlowName} ({workflowId}).");
    }

    private async Task<Guid> EnsureConnectionReference(Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client)
    {
        var query = new QueryExpression("connectionreference") { ColumnSet = new ColumnSet("connectionreferenceid") };
        query.Criteria.AddCondition("connectionreferencelogicalname", ConditionOperator.Equal, options.PowerAutomateConnectionReference);
        var existing = (await client.RetrieveMultipleAsync(query)).Entities.FirstOrDefault();
        var values = new Entity("connectionreference")
        {
            ["connectionreferencedisplayname"] = "FM Central Dataverse",
            ["connectionreferencelogicalname"] = options.PowerAutomateConnectionReference,
            ["connectorid"] = ConnectorId,
            ["connectionid"] = options.PowerAutomateConnectionId,
            ["description"] = "Dataverse connection used by the FM Central SQL synchronization request flow."
        };
        if (existing is null) return await client.CreateAsync(values);
        values.Id = existing.Id;
        await client.UpdateAsync(values);
        return existing.Id;
    }

    private async Task<Guid> EnsurePipelineVariable(Microsoft.PowerPlatform.Dataverse.Client.ServiceClient client)
    {
        var query = new QueryExpression("environmentvariabledefinition") { ColumnSet = new ColumnSet("environmentvariabledefinitionid") };
        query.Criteria.AddCondition("schemaname", ConditionOperator.Equal, PipelineVariable);
        var existing = (await client.RetrieveMultipleAsync(query)).Entities.FirstOrDefault();
        var values = new Entity("environmentvariabledefinition")
        {
            ["displayname"] = "FM Central Sync Pipeline",
            ["schemaname"] = PipelineVariable,
            ["description"] = "Expected Dataverse organization ID and SourceId used to coalesce synchronization requests.",
            ["type"] = new OptionSetValue(100000000),
            ["defaultvalue"] = options.Pipeline
        };
        if (existing is null) return await client.CreateAsync(values);
        values.Id = existing.Id;
        await client.UpdateAsync(values);
        return existing.Id;
    }

    private string BuildClientData(string entitySet)
    {
        const string connectionName = "shared_commondataserviceforapps";
        const string pipelineParameter = "FmcSyncPipeline";
        object openApi(string operationId, object parameters) => new
        {
            type = "OpenApiConnection",
            inputs = new
            {
                host = new { connectionName, operationId, apiId = ConnectorId },
                parameters,
                authentication = "@parameters('$authentication')"
            }
        };

        var list = openApi("ListRecords", new Dictionary<string, object>
        {
            ["entityName"] = entitySet,
            ["$select"] = "fmc_syncrequestid,fmc_name,fmc_status",
            ["$filter"] = $"@concat('fmc_pipeline eq ''', parameters('{pipelineParameter}'), ''' and (fmc_status eq {SyncRequestStatuses.Queued} or fmc_status eq {SyncRequestStatuses.Running})')",
            ["$orderby"] = "createdon asc",
            ["$top"] = 1
        });
        var create = openApi("CreateRecord", new Dictionary<string, object>
        {
            ["entityName"] = entitySet,
            ["item/fmc_name"] = "@concat('SQL Sync ', utcNow())",
            ["item/fmc_command"] = "DrainPending",
            ["item/fmc_pipeline"] = $"@parameters('{pipelineParameter}')",
            ["item/fmc_correlationid"] = "@workflow()?['run']?['name']",
            ["item/fmc_requestedby"] = "@coalesce(triggerOutputs()?['headers']?['x-ms-user-email'], triggerOutputs()?['headers']?['x-ms-user-name'], 'Power Automate')",
            ["item/fmc_status"] = SyncRequestStatuses.Queued
        });

        var actions = new Dictionary<string, object>
        {
            ["List_active_sync_requests"] = WithRunAfter(list, new Dictionary<string, string[]>()),
            ["Initialize_request_id"] = new
            {
                type = "InitializeVariable",
                runAfter = new Dictionary<string, string[]> { ["List_active_sync_requests"] = ["Succeeded"] },
                inputs = new { variables = new[] { new { name = "requestId", type = "string", value = "" } } }
            },
            ["Create_or_reuse_request"] = new
            {
                type = "If",
                runAfter = new Dictionary<string, string[]> { ["Initialize_request_id"] = ["Succeeded"] },
                expression = new { equals = new object[] { "@length(outputs('List_active_sync_requests')?['body/value'])", 0 } },
                actions = new Dictionary<string, object>
                {
                    ["Create_sync_request"] = WithRunAfter(create, new Dictionary<string, string[]>()),
                    ["Set_created_request_id"] = new
                    {
                        type = "SetVariable",
                        runAfter = new Dictionary<string, string[]> { ["Create_sync_request"] = ["Succeeded"] },
                        inputs = new { name = "requestId", value = "@outputs('Create_sync_request')?['body/fmc_syncrequestid']" }
                    }
                },
                @else = new
                {
                    actions = new Dictionary<string, object>
                    {
                        ["Set_existing_request_id"] = new
                        {
                            type = "SetVariable",
                            runAfter = new Dictionary<string, string[]>(),
                            inputs = new { name = "requestId", value = "@first(outputs('List_active_sync_requests')?['body/value'])?['fmc_syncrequestid']" }
                        }
                    }
                }
            },
            ["Request_result"] = new
            {
                type = "Compose",
                runAfter = new Dictionary<string, string[]> { ["Create_or_reuse_request"] = ["Succeeded"] },
                inputs = new { requestId = "@variables('requestId')", status = "QueuedOrRunning" }
            }
        };

        var clientData = new
        {
            properties = new
            {
                connectionReferences = new Dictionary<string, object>
                {
                    [connectionName] = new
                    {
                        runtimeSource = "embedded",
                        connection = new
                        {
                            name = options.PowerAutomateConnectionId,
                            connectionReferenceLogicalName = options.PowerAutomateConnectionReference
                        },
                        api = new { name = connectionName }
                    }
                },
                definition = new
                {
                    schema = "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
                    contentVersion = "1.0.0.0",
                    parameters = new Dictionary<string, object>
                    {
                        ["$connections"] = new { defaultValue = new { }, type = "Object" },
                        ["$authentication"] = new { defaultValue = new { }, type = "SecureObject" },
                        [pipelineParameter] = new
                        {
                            defaultValue = options.Pipeline,
                            type = "String",
                            metadata = new { schemaName = PipelineVariable }
                        }
                    },
                    triggers = new
                    {
                        manual = new
                        {
                            type = "Request", kind = "Button",
                            runtimeConfiguration = new { concurrency = new { runs = 1 } },
                            inputs = new { schema = new { type = "object", properties = new { }, required = Array.Empty<string>() } }
                        }
                    },
                    actions
                }
            },
            schemaVersion = "1.0.0.0"
        };
        var json = JsonSerializer.Serialize(clientData);
        // Anonymous-property names cannot start with '$'; patch the two schema keys after serialization.
        return json.Replace("\"schema\":\"https://schema.management.azure.com/", "\"$schema\":\"https://schema.management.azure.com/");
    }

    private static Dictionary<string, object?> WithRunAfter(object action, Dictionary<string, string[]> runAfter)
    {
        var element = JsonSerializer.SerializeToElement(action);
        var result = element.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        result["runAfter"] = runAfter;
        return result;
    }
}
