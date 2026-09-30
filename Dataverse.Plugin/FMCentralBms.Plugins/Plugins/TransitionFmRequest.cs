#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace FMCentralBms.Plugins
{
    public sealed class TransitionFmRequest : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace; IPluginExecutionContext context; IOrganizationServiceFactory factory;
            PluginServices.ResolveWithFactory(serviceProvider, out trace, out context, out factory);
            if (context.MessageName != S.TransitionApi && context.MessageName != S.DeadlineApi) return;
            FmWorkflowPolicy.Require(context.IsInTransaction, "Workflow commands require a Dataverse transaction.");
            var caller = factory.CreateOrganizationService(context.InitiatingUserId);
            var system = factory.CreateOrganizationService(null);
            var id = (Guid)context.InputParameters["RequestId"];
            var request = caller.Retrieve(S.Request, id, new ColumnSet(true)); // Enforce caller's record access before SYSTEM bookkeeping.
            var revision = request.GetAttributeValue<int>(S.Revision);
            var status = FmWorkflowPolicy.Status(request);
            var now = DateTime.UtcNow;
            var timer = context.MessageName == S.DeadlineApi;
            var command = timer ? "Deadline" : ((string)context.InputParameters["Action"]).Trim();
            var comment = !timer && context.InputParameters.Contains("Comment") ? (string)context.InputParameters["Comment"] ?? "" : "";
            FmWorkflowPolicy.Require(comment.Length <= 4000, "Comment is limited to 4000 characters.");
            var operation = timer ? Guid.NewGuid() : (Guid)context.InputParameters["OperationId"];
            FmWorkflowPolicy.Require(operation != Guid.Empty, "OperationId is required.");
            var email = FmWorkflowStore.UserEmail(system, context.InitiatingUserId);
            if (!timer)
            {
                var receipt = FmWorkflowStore.FindReceipt(system, operation);
                if (receipt != null)
                {
                    FmWorkflowPolicy.Require(receipt.GetAttributeValue<EntityReference>(S.RequestLookup)?.Id == id &&
                        receipt.GetAttributeValue<string>(S.ActorId) == context.InitiatingUserId.ToString("D") &&
                        receipt.GetAttributeValue<string>(S.Command) == command && (receipt.GetAttributeValue<string>(S.Comments) ?? "") == comment,
                        "OperationId was already used for a different command.");
                    Respond(context, receipt.GetAttributeValue<int>(S.ResultStatus), receipt.GetAttributeValue<int>(S.ResultRevision), "Reused committed operation.");
                    return;
                }
                FmWorkflowPolicy.Require((int)context.InputParameters["ExpectedRevision"] == revision, "Request changed. Refresh before deciding.");
            }
            var update = new Entity(S.Request, id) { RowVersion = request.RowVersion };
            FmWorkflowPolicy.Require(!string.IsNullOrEmpty(update.RowVersion), "Concurrency token is missing.");
            var action = 0;
            var stepNumber = request.GetAttributeValue<int>(S.Step);
            var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var detail = comment;
            var owner = request.GetAttributeValue<EntityReference>("ownerid");
            if (command == "Submit")
            {
                FmWorkflowPolicy.Require(status == S.Draft && owner?.Id == context.InitiatingUserId, "Only the owner may submit a Draft request.");
                FmWorkflowPolicy.ValidateDraft(request, true);
                var plan = FmWorkflowStore.ResolvePlan(system, request);
                update[S.Plan] = FmWorkflowPolicy.Serialize(plan);
                update[S.Requester] = email;
                update[S.SubmittedOn] = now;
                status = S.InApproval;
                ActivateStep(update, plan[0], now);
                stepNumber = plan[0].Number;
                recipients.Add(plan[0].Email); recipients.Add(email);
                action = S.SubmitHistory;
                detail = "Submitted. Approval required at step " + stepNumber + ". Due " + ((DateTime)update[S.Due]).ToString("u");
            }
            else if (command == "Approve" || command == "Reject")
            {
                FmWorkflowPolicy.Require(status == S.InApproval, "Request is not awaiting approval.");
                var plan = FmWorkflowPolicy.Deserialize(request.GetAttributeValue<string>(S.Plan));
                var step = plan.Single(s => s.Number == stepNumber);
                var assigned = request.GetAttributeValue<DateTime?>(S.EscalatedOn).HasValue ? step.EscalationUserId : step.UserId;
                FmWorkflowPolicy.Require(assigned == context.InitiatingUserId && FmWorkflowStore.HasRole(system, assigned, step.Role), "Only the currently assigned approver with the configured role may decide.");
                FmWorkflowPolicy.Require(!string.IsNullOrWhiteSpace(comment), "A decision comment is required.");
                recipients.Add(request.GetAttributeValue<string>(S.Requester));
                if (command == "Reject") { status = S.Rejected; action = S.RejectHistory; Finish(update, now); }
                else
                {
                    action = S.ApproveHistory;
                    var next = plan.FirstOrDefault(s => s.Number > step.Number);
                    if (next == null) { status = S.Approved; Finish(update, now); }
                    else
                    {
                        // Re-check active membership before assigning the next step.
                        FmWorkflowStore.UserEmail(system, next.UserId);
                        FmWorkflowPolicy.Require(FmWorkflowStore.HasRole(system, next.UserId, next.Role), "Next approver no longer has the required role.");
                        ActivateStep(update, next, now); recipients.Add(next.Email);
                    }
                }
                detail = command + " at step " + stepNumber + ": " + comment + (status == S.InApproval ? ". Waiting for the next approver." : "");
            }
            else if (command == "Close")
            {
                FmWorkflowPolicy.Require(owner?.Id == context.InitiatingUserId && (status == S.Approved || status == S.Rejected), "Only the owner may close an Approved or Rejected request.");
                FmWorkflowPolicy.Require(!string.IsNullOrWhiteSpace(comment), "A closure comment is required.");
                status = S.Closed; action = S.CloseHistory; Finish(update, now);
                recipients.Add(request.GetAttributeValue<string>(S.Requester));
            }
            else if (timer)
            {
                // The configured Power Automate connection owner must be an administrator;
                // the API cannot be used by an Operator to manipulate deadlines.
                FmWorkflowPolicy.Require(FmWorkflowStore.HasRole(system, context.InitiatingUserId, "System Administrator"), "Only the automation administrator may process deadlines.");
                if (status != S.InApproval) { Respond(context, status, revision, "No pending approval."); return; }
                var step = FmWorkflowPolicy.Deserialize(request.GetAttributeValue<string>(S.Plan)).Single(s => s.Number == stepNumber);
                var due = request.GetAttributeValue<DateTime>(S.Due);
                if (now >= due.AddHours(step.EscalationHours) && !request.GetAttributeValue<DateTime?>(S.EscalatedOn).HasValue)
                {
                    FmWorkflowStore.UserEmail(system, step.EscalationUserId);
                    FmWorkflowPolicy.Require(FmWorkflowStore.HasRole(system, step.EscalationUserId, step.Role), "Escalation user no longer holds the required role.");
                    update[S.EscalatedOn] = now; update[S.Approver] = step.EscalationEmail;
                    if (!request.GetAttributeValue<DateTime?>(S.OverdueOn).HasValue) update[S.OverdueOn] = now;
                    command = "Escalate"; action = S.EscalateHistory;
                    recipients.Add(step.EscalationEmail); recipients.Add(request.GetAttributeValue<string>(S.Requester));
                    detail = "Overdue since " + due.ToString("u") + ". Approval reassigned to " + step.EscalationEmail;
                }
                else if (now >= due && !request.GetAttributeValue<DateTime?>(S.OverdueOn).HasValue)
                {
                    update[S.OverdueOn] = now; command = "Overdue"; action = S.OverdueHistory;
                    recipients.Add(request.GetAttributeValue<string>(S.Approver)); recipients.Add(request.GetAttributeValue<string>(S.Requester));
                    detail = "Approval overdue since " + due.ToString("u") + ". Escalation scheduled at " + due.AddHours(step.EscalationHours).ToString("u");
                }
                else if (now >= due.AddHours(-step.ReminderHours) && now < due &&
                    !request.GetAttributeValue<DateTime?>(S.ReminderOn).HasValue && !request.GetAttributeValue<DateTime?>(S.EscalatedOn).HasValue)
                {
                    update[S.ReminderOn] = now; command = "Remind"; action = S.RemindHistory;
                    recipients.Add(request.GetAttributeValue<string>(S.Approver));
                    detail = "Approval reminder. Due " + due.ToString("u");
                }
                else { Respond(context, status, revision, "No deadline action due."); return; }
                comment = detail;
            }
            else throw new InvalidPluginExecutionException("Unknown FM workflow action.");

            update[S.Status] = new OptionSetValue(status);
            update[S.LegacyStatus] = new OptionSetValue(FmWorkflowPolicy.Legacy(status));
            update[S.Revision] = checked(revision + 1);
            caller.Execute(new UpdateRequest { Target = update, ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches });
            FmWorkflowStore.History(system, request, operation, context.InitiatingUserId, email, command, comment, action, revision + 1, status, stepNumber);
            foreach (var recipient in recipients)
                FmWorkflowStore.Email(system, request, operation, recipient, "[FMC] FM Request " + command, detail, command);
            trace?.Trace("FM workflow {0}: request={1}, revision={2}, operation={3}", command, id, revision + 1, operation);
            Respond(context, status, revision + 1, command + " completed.");
        }

        private static void ActivateStep(Entity update, FmApprovalStep step, DateTime now)
        {
            update[S.Step] = step.Number; update[S.Approver] = step.Email; update[S.Due] = now.AddDays(step.SlaDays);
            update[S.ReminderOn] = null; update[S.OverdueOn] = null; update[S.EscalatedOn] = null;
        }
        private static void Finish(Entity update, DateTime now)
        { update[S.CompletedOn] = now; update[S.Approver] = null; update[S.Due] = null; }
        private static void Respond(IPluginExecutionContext context, int status, int revision, string message)
        { context.OutputParameters["Status"] = status; context.OutputParameters["Revision"] = revision; context.OutputParameters["Message"] = message; }
    }
}
