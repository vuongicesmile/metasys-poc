#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace FMCentralBms.Plugins
{
    // Synchronous PreOperation: applies equally to forms, imports and direct Web API.
    public sealed class GuardFmWorkflow : IPlugin
    {
        internal static readonly string[] Protected = { S.Status, S.LegacyStatus, S.Requester, S.Approver, S.Step, S.Due,
            S.SubmittedOn, S.CompletedOn, S.Plan, S.Revision, S.ReminderOn, S.OverdueOn, S.EscalatedOn };
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace; IPluginExecutionContext context; IOrganizationServiceFactory factory;
            PluginServices.ResolveWithFactory(serviceProvider, out trace, out context, out factory);
            var system = factory.CreateOrganizationService(null);
            var internalWrite = IsWorkflowWrite(context);
            for (var scope = context; scope != null; scope = scope.ParentContext)
                trace?.Trace("FM guard scope: message={0}, stage={1}, depth={2}, marker={3}, transaction={4}", scope.MessageName, scope.Stage, scope.Depth,
                    scope.SharedVariables.Contains("FmcWorkflowWrite") ? scope.SharedVariables["FmcWorkflowWrite"] : "absent", scope.IsInTransaction);
            if (context.PrimaryEntityName == S.History)
            {
                FmWorkflowPolicy.Require(context.MessageName == "Create" && internalWrite, "Request history is append-only and written by workflow commands.");
                return;
            }
            if (context.PrimaryEntityName == S.Route)
            {
                FmWorkflowPolicy.Require(FmWorkflowStore.HasRole(system, context.InitiatingUserId, "System Administrator"), "Only workflow administrators may configure approval routes.");
                return;
            }
            if (context.PrimaryEntityName != S.Request || internalWrite) return;
            if (context.MessageName == "Create")
            {
                var target = (Entity)context.InputParameters["Target"];
                FmWorkflowPolicy.Require(FmWorkflowPolicy.Status(target) == S.Draft &&
                    (!target.Contains(S.LegacyStatus) || FmWorkflowPolicy.Choice(target, S.LegacyStatus) == 100000000), "New requests must be Draft.");
                FmWorkflowPolicy.Require(!Protected.Where(f => f != S.Status && f != S.LegacyStatus).Any(f => target.Contains(f) && target[f] != null), "Workflow-managed fields cannot be supplied on Create.");
                FmWorkflowPolicy.ValidateDraft(target, false);
                target[S.Status] = new OptionSetValue(S.Draft); target[S.LegacyStatus] = new OptionSetValue(100000000);
                target[S.Revision] = 0;
                if (!target.Contains(S.Type)) target[S.Type] = new OptionSetValue(S.Ciwg);
                return;
            }
            var existing = system.Retrieve(S.Request, context.PrimaryEntityId, new ColumnSet(true));
            if (context.MessageName == "Update" || context.MessageName == "Delete" || context.MessageName == "Assign")
            {
                FmWorkflowPolicy.Require(FmWorkflowPolicy.Status(existing) == S.Draft, "Only Draft requests may be edited/deleted. Use workflow actions to change status.");
                FmWorkflowPolicy.Require(existing.GetAttributeValue<EntityReference>("ownerid")?.Id == context.InitiatingUserId,
                    "Only the request owner may edit/delete a Draft.");
            }
            if (context.MessageName == "Update")
            {
                var target = (Entity)context.InputParameters["Target"];
                foreach (var field in Protected)
                    FmWorkflowPolicy.Require(!target.Contains(field) || Equal(target[field], existing.Contains(field) ? existing[field] : null), "Use workflow actions; field is managed: " + field);
                foreach (var pair in target.Attributes) existing[pair.Key] = pair.Value;
                FmWorkflowPolicy.ValidateDraft(existing, false);
                target[S.Revision] = checked(existing.GetAttributeValue<int>(S.Revision) + 1);
            }
        }
        private static bool Equal(object left, object right)
        { return left is OptionSetValue a && right is OptionSetValue b ? a.Value == b.Value : Equals(left, right); }
        internal static bool IsWorkflowWrite(IPluginExecutionContext context)
        {
            // Dataverse serializes the Custom API parent before its main plug-in
            // changes SharedVariables. Trust only the platform-supplied synchronous
            // main-operation ancestry AND the exact request being processed.
            var target = context.InputParameters.Contains("Target") ? context.InputParameters["Target"] as Entity : null;
            var requestId = context.PrimaryEntityName == S.Request && context.MessageName == "Update" ? context.PrimaryEntityId :
                context.PrimaryEntityName == S.History && context.MessageName == "Create" ? target?.GetAttributeValue<EntityReference>(S.RequestLookup)?.Id ?? Guid.Empty : Guid.Empty;
            if (requestId == Guid.Empty || !context.IsInTransaction) return false;
            for (var parent = context.ParentContext; parent != null; parent = parent.ParentContext)
                if ((parent.MessageName == S.TransitionApi || parent.MessageName == S.DeadlineApi) &&
                    parent.Stage == 30 && parent.IsInTransaction && parent.InitiatingUserId == context.InitiatingUserId &&
                    parent.InputParameters.Contains("RequestId") && Equals(parent.InputParameters["RequestId"], requestId)) return true;
            return false;
        }
    }
}
