using Microsoft.PowerPlatform.Dataverse.Client;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace Dataverse.SyncWorker.DataAccess.Services;

public sealed partial class DataversePluginProvisioner
{
    private static async Task RegisterFmWorkflow(ServiceClient client, Guid assemblyId, Guid solutionId)
    {
        var command = await EnsurePluginType(client, assemblyId, "FMCentralBms.Plugins.TransitionFmRequest", "FM Workflow Commands");
        var guard = await EnsurePluginType(client, assemblyId, "FMCentralBms.Plugins.GuardFmWorkflow", "FM Workflow Guard");
        foreach (var table in new[] { S.Request, S.Route, S.History })
        foreach (var messageName in table == S.Request ? new[] { "Create", "Update", "Delete", "Assign" } : new[] { "Create", "Update", "Delete" })
        {
            var message = await FindMessage(client, messageName);
            var filter = await FindMessageFilter(client, message.Id, table);
            var definition = new StepDefinition($"FM Workflow: {table} {messageName}", messageName, null, table, guard.Id, 0, 20,
                "Enforces lifecycle, immutable history and route administration for every client.");
            var step = await EnsureStep(client, definition, message.Id, filter.Id, guard.Id);
            await AddToSolution(client, solutionId, step.Id, StepComponent, definition.Name, false);
        }
        foreach (var api in new[] { S.TransitionApi, S.DeadlineApi })
        {
            var id = await EnsureUnboundCustomApi(client, api, api == S.TransitionApi ? "FM Request Action" : "Process FM Request Deadline",
                "Transactional FM workflow with concurrency, authorization, immutable history and email outbox.", "prvWritefmc_fmrequest", command.Id);
            await EnsureApiProperty(client, api, "customapirequestparameter", id, "RequestId", "Request ID", 12, false);
            if (api == S.TransitionApi)
            {
                await EnsureApiProperty(client, api, "customapirequestparameter", id, "Action", "Action", 10, false);
                await EnsureApiProperty(client, api, "customapirequestparameter", id, "Comment", "Comment", 10, true);
                await EnsureApiProperty(client, api, "customapirequestparameter", id, "ExpectedRevision", "Expected Revision", 7, false);
                await EnsureApiProperty(client, api, "customapirequestparameter", id, "OperationId", "Operation ID", 12, false);
            }
            await EnsureApiProperty(client, api, "customapiresponseproperty", id, "Status", "Status", 7);
            await EnsureApiProperty(client, api, "customapiresponseproperty", id, "Revision", "Revision", 7);
            await EnsureApiProperty(client, api, "customapiresponseproperty", id, "Message", "Message", 10);
        }
    }
}
