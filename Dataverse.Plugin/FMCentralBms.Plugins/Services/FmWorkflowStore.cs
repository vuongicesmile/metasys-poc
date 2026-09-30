#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace FMCentralBms.Plugins
{
    internal static class FmWorkflowStore
    {
        public static Entity ActiveUser(IOrganizationService service, string email)
        {
            FmWorkflowPolicy.Require(!string.IsNullOrWhiteSpace(email), "Configure approver and escalation email on every selected route.");
            var q = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("internalemailaddress"), TopCount = 2 };
            q.Criteria.AddCondition("internalemailaddress", ConditionOperator.Equal, email.Trim());
            q.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            var users = service.RetrieveMultiple(q).Entities;
            FmWorkflowPolicy.Require(users.Count == 1, "Email must resolve to exactly one active Dataverse user: " + email);
            return users[0];
        }

        public static string UserEmail(IOrganizationService service, Guid id)
        {
            var user = service.Retrieve("systemuser", id, new ColumnSet("internalemailaddress", "isdisabled"));
            var email = user.GetAttributeValue<string>("internalemailaddress");
            FmWorkflowPolicy.Require(!user.GetAttributeValue<bool>("isdisabled") && !string.IsNullOrWhiteSpace(email), "Active user with an email is required.");
            return email;
        }

        public static bool HasRole(IOrganizationService service, Guid userId, string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName)) return true;
            foreach (var viaTeam in new[] { false, true })
            {
                var q = new QueryExpression("role") { ColumnSet = new ColumnSet(false), TopCount = 1 };
                q.Criteria.AddCondition("name", ConditionOperator.Equal, roleName);
                if (viaTeam)
                {
                    var roles = q.AddLink("teamroles", "roleid", "roleid");
                    roles.AddLink("teammembership", "teamid", "teamid").LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
                }
                else q.AddLink("systemuserroles", "roleid", "roleid").LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
                if (service.RetrieveMultiple(q).Entities.Count > 0) return true;
            }
            return false;
        }

        public static List<FmApprovalStep> ResolvePlan(IOrganizationService service, Entity request)
        {
            var q = new QueryExpression(S.Route) { ColumnSet = new ColumnSet(true), PageInfo = new PagingInfo { Count = 500, PageNumber = 1 } };
            q.Criteria.AddCondition(S.Active, ConditionOperator.Equal, true);
            q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            q.Criteria.AddCondition(S.Type, ConditionOperator.Equal, FmWorkflowPolicy.Choice(request, S.Type, S.Ciwg));
            var rows = new List<Entity>();
            do
            {
                var page = service.RetrieveMultiple(q); rows.AddRange(page.Entities);
                FmWorkflowPolicy.Require(rows.Count <= 2000, "Too many matching route definitions; narrow the configuration.");
                if (!page.MoreRecords) break;
                q.PageInfo.PageNumber++; q.PageInfo.PagingCookie = page.PagingCookie;
            } while (true);
            return FmWorkflowPolicy.SelectRoutes(request, rows).Select(r =>
            {
                var user = ActiveUser(service, r.GetAttributeValue<string>(S.RouteApprover));
                var manager = ActiveUser(service, r.GetAttributeValue<string>(S.Escalation));
                var role = r.GetAttributeValue<string>(S.Role);
                FmWorkflowPolicy.Require(HasRole(service, user.Id, role) && HasRole(service, manager.Id, role), "Assigned approver/escalation user must hold the configured approver role.");
                var days = r.GetAttributeValue<int>(S.Sla);
                var reminder = r.Contains(S.ReminderHours) ? r.GetAttributeValue<int>(S.ReminderHours) : 24;
                var escalation = r.GetAttributeValue<int>(S.EscalationHours);
                FmWorkflowPolicy.Require(days >= 1 && days <= 365 && reminder >= 0 && reminder <= days * 24 && escalation >= 0 && escalation <= 8760, "Invalid SLA/reminder/escalation configuration.");
                return new FmApprovalStep { Number = r.GetAttributeValue<int>(S.StepNo), UserId = user.Id,
                    Email = user.GetAttributeValue<string>("internalemailaddress"), EscalationUserId = manager.Id,
                    EscalationEmail = manager.GetAttributeValue<string>("internalemailaddress"), Role = role,
                    SlaDays = days, ReminderHours = reminder, EscalationHours = escalation };
            }).ToList();
        }

        public static Entity FindReceipt(IOrganizationService service, Guid operationId)
        {
            var q = new QueryExpression(S.History) { ColumnSet = new ColumnSet(true), TopCount = 1 };
            q.Criteria.AddCondition(S.History + "id", ConditionOperator.Equal, operationId);
            return service.RetrieveMultiple(q).Entities.FirstOrDefault();
        }

        public static void History(IOrganizationService system, Entity request, Guid operation, Guid actor, string email,
            string command, string comment, int action, int revision, int status, int step)
        {
            system.Create(new Entity(S.History, operation) {
                [S.Name] = (command + " - " + request.GetAttributeValue<string>(S.Name)).Substring(0, Math.Min(200, (command + " - " + request.GetAttributeValue<string>(S.Name)).Length)),
                [S.RequestLookup] = request.ToEntityReference(), [S.Action] = new OptionSetValue(action),
                [S.ActorId] = actor.ToString("D"), [S.ActorEmail] = email, [S.Comments] = comment,
                [S.ActedOn] = DateTime.UtcNow, [S.Command] = command, [S.ResultRevision] = revision,
                [S.ResultStatus] = status, [S.StepNo] = step
            });
        }

        public static void Email(IOrganizationService system, Entity request, Guid operation, string recipient, string subject, string detail, string command)
        {
            FmWorkflowPolicy.Require(!string.IsNullOrWhiteSpace(recipient), "Notification recipient is missing.");
            var env = new QueryExpression("environmentvariabledefinition") { ColumnSet = new ColumnSet("defaultvalue"), TopCount = 1 };
            env.Criteria.AddCondition("schemaname", ConditionOperator.Equal, "fmc_FmAppUrl");
            var definition = system.RetrieveMultiple(env).Entities.SingleOrDefault();
            FmWorkflowPolicy.Require(definition != null, "Configure fmc_FmAppUrl before enabling workflow.");
            var values = new QueryExpression("environmentvariablevalue") { ColumnSet = new ColumnSet("value"), TopCount = 2 };
            values.Criteria.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, definition.Id);
            var current = system.RetrieveMultiple(values).Entities;
            FmWorkflowPolicy.Require(current.Count <= 1, "Ambiguous fmc_FmAppUrl current values.");
            var appUrl = current.Count == 1 ? current[0].GetAttributeValue<string>("value") : definition.GetAttributeValue<string>("defaultvalue");
            Uri parsed;
            FmWorkflowPolicy.Require(Uri.TryCreate(appUrl, UriKind.Absolute, out parsed) && parsed.Scheme == "https", "FM app URL must be HTTPS.");
            var link = appUrl + "&pagetype=entityrecord&etn=" + S.Request + "&id=" + request.Id.ToString("D");
            var body = BuildEmailBody(request, link, command, detail);
            system.Create(new Entity("fmc_notification") {
                ["fmc_name"] = subject, ["fmc_channel"] = new OptionSetValue(789120000),
                ["fmc_status"] = new OptionSetValue(789121000), ["fmc_eventtype"] = new OptionSetValue(EventType(command)),
                ["fmc_recipientemail"] = recipient, ["fmc_subject"] = subject,
                ["fmc_body"] = body,
                ["fmc_correlationkey"] = CorrelationKey(operation, recipient),
                ["fmc_regardingtable"] = S.Request, ["fmc_regardingid"] = request.Id.ToString("D"), ["fmc_attempts"] = 0
            });
        }

        internal static string BuildEmailBody(Entity request, string link, string command, string detail)
        {
            var name = WebUtility.HtmlEncode(request.GetAttributeValue<string>(S.Name) ?? "FM Request");
            var status = FmWorkflowPolicy.Status(request);
            var statusText = StatusText(status);
            var statusColor = StatusColor(status);
            var commandText = CommandText(command);
            var step = request.Contains(S.Step) ? request.GetAttributeValue<int>(S.Step).ToString() : "—";
            var approver = request.GetAttributeValue<string>(S.Approver) ?? "—";
            var due = request.GetAttributeValue<DateTime?>(S.Due).HasValue
                ? request.GetAttributeValue<DateTime>(S.Due).ToUniversalTime().ToString("dd/MM/yyyy HH:mm 'UTC'") : "—";
            var safeDetail = WebUtility.HtmlEncode(detail ?? "").Replace("\r\n", "\n").Replace("\n", "<br>");
            var safeLink = WebUtility.HtmlEncode(link);
            return "<!doctype html><html><body style=\"margin:0;padding:0;background:#f3f6fa;font-family:Segoe UI,Arial,sans-serif;color:#172b4d;\">" +
                "<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#f3f6fa;padding:28px 12px;\"><tr><td align=\"center\">" +
                "<table role=\"presentation\" width=\"620\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;max-width:620px;background:#ffffff;border:1px solid #dfe7f1;border-radius:14px;overflow:hidden;\">" +
                "<tr><td style=\"padding:22px 28px;background:#123b68;color:#ffffff;\"><div style=\"font-size:11px;letter-spacing:2px;font-weight:700;color:#b9dcff;\">FMC · FACILITIES WORKFLOW</div>" +
                "<div style=\"font-size:22px;font-weight:700;margin-top:8px;\">FM Request update</div><div style=\"font-size:13px;color:#d9eaff;margin-top:5px;\">" + WebUtility.HtmlEncode(commandText) + "</div></td></tr>" +
                "<tr><td style=\"padding:26px 28px 10px;\"><div style=\"font-size:20px;font-weight:700;color:#172b4d;\">" + name + "</div>" +
                "<div style=\"margin-top:12px;display:inline-block;padding:6px 11px;border-radius:20px;background:" + statusColor + ";color:#ffffff;font-size:12px;font-weight:700;\">" + WebUtility.HtmlEncode(statusText) + "</div></td></tr>" +
                "<tr><td style=\"padding:10px 28px 8px;\"><table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr>" +
                InfoCell("Current step", step) + InfoCell("Approver", approver) + InfoCell("Due", due) + "</tr></table></td></tr>" +
                "<tr><td style=\"padding:16px 28px 6px;\"><div style=\"font-size:12px;color:#6b7f96;font-weight:700;text-transform:uppercase;letter-spacing:.6px;\">Workflow note</div>" +
                "<div style=\"margin-top:8px;padding:14px 16px;background:#f6f8fb;border-left:4px solid #2f80d0;border-radius:6px;font-size:14px;line-height:1.55;\">" + safeDetail + "</div></td></tr>" +
                "<tr><td style=\"padding:22px 28px 12px;\"><a href=\"" + safeLink + "\" style=\"display:inline-block;background:#1769d1;color:#ffffff;text-decoration:none;font-weight:700;font-size:14px;padding:12px 20px;border-radius:8px;\">Open FM Request &amp; review</a>" +
                "<div style=\"margin-top:12px;font-size:12px;color:#6b7f96;line-height:1.5;\">Open the request in FMC BMS Demo to review the history and available workflow action.</div></td></tr>" +
                "<tr><td style=\"padding:18px 28px;background:#f8fafc;border-top:1px solid #e7edf4;color:#71839a;font-size:11px;line-height:1.5;\">This is an automated message from FMCentralBms. Please do not reply to this email.</td></tr>" +
                "</table></td></tr></table></body></html>";
        }

        private static string InfoCell(string label, string value)
        { return "<td width=\"33%\" valign=\"top\" style=\"padding:10px 8px 10px 0;border-top:1px solid #e7edf4;\"><div style=\"font-size:11px;color:#71839a;\">" + WebUtility.HtmlEncode(label) + "</div><div style=\"margin-top:5px;font-size:13px;font-weight:700;color:#172b4d;word-break:break-word;\">" + WebUtility.HtmlEncode(value) + "</div></td>"; }

        private static string CommandText(string command)
        {
            switch (command) {
                case "Submit": return "A new approval is waiting for review.";
                case "Approve": return "An approval step has been completed.";
                case "Reject": return "The request was rejected.";
                case "Close": return "The request was closed.";
                case "Remind": return "An approval is approaching its deadline.";
                case "Overdue": return "An approval is overdue.";
                case "Escalate": return "An overdue approval was escalated.";
                default: return "The request workflow has changed.";
            }
        }

        private static string StatusText(int status)
        {
            switch (status) { case S.Draft: return "Draft"; case S.InApproval: return "In Approval"; case S.Approved: return "Approved"; case S.Rejected: return "Rejected"; case S.Closed: return "Closed"; default: return "Submitted"; }
        }

        private static string StatusColor(int status)
        {
            switch (status) { case S.Approved: return "#16805c"; case S.Rejected: return "#b42335"; case S.Closed: return "#66788f"; case S.InApproval: return "#c77800"; default: return "#4267a8"; }
        }

        internal static string CorrelationKey(Guid operation, string recipient)
        {
            using (var hash = SHA256.Create())
                return "fm:" + operation.ToString("N") + ":" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(recipient.ToLowerInvariant()))).Replace("-", "");
        }

        internal static int EventType(string command)
        {
            switch (command)
            {
                case "Submit": return 789122010;
                case "Approve": return 789122011;
                case "Reject": return 789122012;
                case "Overdue": return 789122013;
                case "Escalate": return 789122014;
                case "Remind": return 789122015;
                case "Close": return 789122016;
                default: throw new InvalidPluginExecutionException("Unknown notification event.");
            }
        }
    }
}
