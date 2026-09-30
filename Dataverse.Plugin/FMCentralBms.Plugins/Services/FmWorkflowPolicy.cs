#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace FMCentralBms.Plugins
{
    internal sealed class FmApprovalStep
    {
        public int Number, SlaDays, ReminderHours, EscalationHours;
        public Guid UserId, EscalationUserId;
        public string Email, EscalationEmail, Role;
    }

    internal static class FmWorkflowPolicy
    {
        public static int Choice(Entity row, string field, int fallback = 0)
        { return row.GetAttributeValue<OptionSetValue>(field)?.Value ?? fallback; }

        public static void Require(bool allowed, string message)
        { if (!allowed) throw new InvalidPluginExecutionException("FM-WORKFLOW: " + message); }

        public static int Status(Entity row)
        {
            if (row.Contains(S.Status)) return Choice(row, S.Status);
            switch (Choice(row, S.LegacyStatus, 100000000))
            {
                case 100000001: return S.Submitted;
                case 100000002: return S.Approved;
                case 100000003: return S.Rejected;
                case 100000004: return S.Closed;
                default: return S.Draft;
            }
        }

        public static int Legacy(int status)
        {
            switch (status)
            {
                case S.Submitted: case S.InApproval: return 100000001;
                case S.Approved: return 100000002;
                case S.Rejected: return 100000003;
                case S.Closed: return 100000004;
                default: return 100000000;
            }
        }

        public static void ValidateDraft(Entity row, bool submitting)
        {
            Require(!string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(S.Name)), "Name is required.");
            var type = Choice(row, S.Type, S.Ciwg);
            Require(type == S.Ciwg || type == S.Risk || type == S.Project, "Unknown request type.");
            Require(row.GetAttributeValue<decimal>(S.Value) >= 0, "Estimated value cannot be negative.");
            foreach (var field in new[] { S.Likelihood, S.Impact })
                if (row.Contains(field) && row[field] != null)
                    Require(row.GetAttributeValue<int>(field) >= 1 && row.GetAttributeValue<int>(field) <= 5, "Risk likelihood and impact must be 1–5.");
            if (submitting)
            {
                Require(!string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(S.Description)), "Description is required before submission.");
                if (type == S.Risk)
                {
                    Require(row.GetAttributeValue<int>(S.Likelihood) >= 1 && row.GetAttributeValue<int>(S.Impact) >= 1,
                        "Risk requests require likelihood and impact.");
                    Require(!string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(S.Mitigation)) && row.GetAttributeValue<DateTime?>(S.ReviewDate).HasValue,
                        "Risk requests require mitigation and review date.");
                    Require(row.GetAttributeValue<EntityReference>("ownerid")?.LogicalName == "systemuser", "Assign a named risk owner before submission.");
                }
            }
        }

        // Matching rules are cumulative across steps. At a step, choose the most
        // specific department, then the greatest qualifying thresholds. Ties fail.
        public static IList<Entity> SelectRoutes(Entity request, IEnumerable<Entity> routes)
        {
            var department = request.GetAttributeValue<string>(S.Department) ?? "";
            var value = request.GetAttributeValue<decimal>(S.Value);
            var severity = Choice(request, S.Severity, S.Low);
            var matches = routes.Where(r => r.GetAttributeValue<bool>(S.Active) &&
                Choice(r, S.Type) == Choice(request, S.Type, S.Ciwg) &&
                (string.IsNullOrWhiteSpace(r.GetAttributeValue<string>(S.Department)) ||
                 string.Equals(department, r.GetAttributeValue<string>(S.Department), StringComparison.OrdinalIgnoreCase)) &&
                r.GetAttributeValue<decimal>(S.MinValue) <= value && Choice(r, S.MinSeverity, S.Low) <= severity);
            var selected = new List<Entity>();
            foreach (var group in matches.GroupBy(r => r.GetAttributeValue<int>(S.StepNo)).OrderBy(g => g.Key))
            {
                Require(group.Key > 0, "Route step number must be positive.");
                var ordered = group.OrderByDescending(r => !string.IsNullOrWhiteSpace(r.GetAttributeValue<string>(S.Department)))
                    .ThenByDescending(r => r.GetAttributeValue<decimal>(S.MinValue)).ThenByDescending(r => Choice(r, S.MinSeverity, S.Low)).ToList();
                var first = ordered[0];
                if (ordered.Count > 1)
                {
                    var second = ordered[1];
                    Require(!(string.IsNullOrWhiteSpace(first.GetAttributeValue<string>(S.Department)) == string.IsNullOrWhiteSpace(second.GetAttributeValue<string>(S.Department)) &&
                        first.GetAttributeValue<decimal>(S.MinValue) == second.GetAttributeValue<decimal>(S.MinValue) &&
                        Choice(first, S.MinSeverity, S.Low) == Choice(second, S.MinSeverity, S.Low)), "Ambiguous approval routes at step " + group.Key + ".");
                }
                selected.Add(first);
            }
            Require(selected.Count > 0 && selected.Count <= 20, "Configure 1–20 matching approval steps before submitting.");
            return selected;
        }

        public static string Serialize(IEnumerable<FmApprovalStep> steps)
        {
            return new XElement("steps", steps.Select(s => new XElement("step",
                new XAttribute("number", s.Number), new XAttribute("user", s.UserId), new XAttribute("email", s.Email),
                new XAttribute("escalationUser", s.EscalationUserId), new XAttribute("escalationEmail", s.EscalationEmail),
                new XAttribute("role", s.Role ?? ""), new XAttribute("days", s.SlaDays),
                new XAttribute("reminder", s.ReminderHours), new XAttribute("escalation", s.EscalationHours)))).ToString(SaveOptions.DisableFormatting);
        }

        public static List<FmApprovalStep> Deserialize(string xml)
        {
            Require(!string.IsNullOrEmpty(xml), "Request has no approval snapshot; submit through the workflow command.");
            return XElement.Parse(xml).Elements("step").Select(e => new FmApprovalStep
            {
                Number = (int)e.Attribute("number"), UserId = (Guid)e.Attribute("user"), Email = (string)e.Attribute("email"),
                EscalationUserId = (Guid)e.Attribute("escalationUser"), EscalationEmail = (string)e.Attribute("escalationEmail"),
                Role = (string)e.Attribute("role"), SlaDays = (int)e.Attribute("days"),
                ReminderHours = (int)e.Attribute("reminder"), EscalationHours = (int)e.Attribute("escalation")
            }).ToList();
        }
    }
}
