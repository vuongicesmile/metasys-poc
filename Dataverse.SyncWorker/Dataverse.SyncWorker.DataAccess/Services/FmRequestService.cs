using Microsoft.Xrm.Sdk;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace Dataverse.SyncWorker.DataAccess.Services;

// Integration adapter only. Dataverse plug-ins enforce rules for all clients.
public sealed class FmRequestService(DataverseConnection connection)
{
    public Guid CreateFmRequest(string name, string description) => connection.Get().Create(new Entity(S.Request)
    {
        [S.Name] = name, [S.Description] = description, [S.Type] = new OptionSetValue(S.Ciwg),
        [S.Status] = new OptionSetValue(S.Draft), [S.LegacyStatus] = new OptionSetValue(100000000)
    });

    public OrganizationResponse Transition(Guid requestId, string action, string comment, int expectedRevision, Guid operationId)
    {
        var command = new OrganizationRequest(S.TransitionApi) {
            ["RequestId"] = requestId, ["Action"] = action, ["Comment"] = comment,
            ["ExpectedRevision"] = expectedRevision, ["OperationId"] = operationId
        };
        return connection.Get().Execute(command);
    }

    public void UpdateDraftFmRequest(Guid requestId, string name, string description) => connection.Get().Update(new Entity(S.Request, requestId) { [S.Name] = name, [S.Description] = description });
    public void DeleteDraftFmRequest(Guid requestId) => connection.Get().Delete(S.Request, requestId);
}
