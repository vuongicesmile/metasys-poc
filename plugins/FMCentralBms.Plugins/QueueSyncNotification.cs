using System;
using System.Globalization;
using System.ServiceModel;
using System.Security;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Creates an idempotent notification outbox record when a SQL sync request
    /// reaches a terminal state. Email delivery is handled asynchronously by
    /// Power Automate and never participates in the sync transaction.
    /// </summary>
    public sealed class QueueSyncNotification : IPlugin
    {
        private const string RequestTable = "fmc_syncrequest";
        private const string NotificationTable = "fmc_notification";
        private const int Succeeded = 789100002;
        private const int CompletedWithIssues = 789100003;
        private const int Failed = 789100004;
        private const int EmailChannel = 789120000;
        private const int Pending = 789121000;
        private const int Skipped = 789121003;

        public void Execute(IServiceProvider serviceProvider)
        {
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(context.PrimaryEntityName, RequestTable, StringComparison.OrdinalIgnoreCase) ||
                context.PrimaryEntityId == Guid.Empty)
                return;

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            var status = target == null
                ? null
                : target.GetAttributeValue<OptionSetValue>("fmc_status");
            if (status == null || !IsTerminal(status.Value))
                return;

            var service = factory.CreateOrganizationService(context.UserId);
            var correlationKey = string.Format(CultureInfo.InvariantCulture,
                "syncrequest:{0:N}:status:{1}", context.PrimaryEntityId, status.Value);
            if (FindByCorrelation(service, correlationKey) != null)
            {
                trace.Trace("QueueSyncNotification DUPLICATE CorrelationKey={0}", correlationKey);
                return;
            }

            var request = service.Retrieve(RequestTable, context.PrimaryEntityId,
                new ColumnSet("fmc_name", "fmc_status", "fmc_requestedby", "fmc_deliveredrows",
                    "fmc_quarantinedrows", "fmc_pendingafter", "fmc_errormessage", "createdby"));
            var recipient = ResolveRecipient(service, request, trace);
            var eventType = EventType(status.Value);
            var label = StatusLabel(status.Value);
            var notification = new Entity(NotificationTable)
            {
                ["fmc_name"] = "SQL sync " + label,
                ["fmc_channel"] = new OptionSetValue(EmailChannel),
                ["fmc_eventtype"] = new OptionSetValue(eventType),
                ["fmc_status"] = new OptionSetValue(recipient.Email == null ? Skipped : Pending),
                ["fmc_recipientemail"] = recipient.Email,
                ["fmc_recipientname"] = recipient.Name,
                ["fmc_subject"] = Subject(status.Value),
                ["fmc_body"] = Body(request, label),
                ["fmc_correlationkey"] = correlationKey,
                ["fmc_regardingtable"] = RequestTable,
                ["fmc_regardingid"] = context.PrimaryEntityId.ToString("D"),
                ["fmc_attempts"] = 0,
                ["fmc_errormessage"] = recipient.Email == null
                    ? "Notification skipped because no valid requester email was available."
                    : null
            };

            try
            {
                var notificationId = service.Create(notification);
                trace.Trace("QueueSyncNotification CREATED NotificationId={0}; Status={1}; HasRecipient={2}",
                    notificationId, status.Value, recipient.Email != null);
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                if (FindByCorrelation(service, correlationKey) != null)
                {
                    trace.Trace("QueueSyncNotification RACE_REUSE CorrelationKey={0}", correlationKey);
                    return;
                }
                throw;
            }
        }

        private static Recipient ResolveRecipient(
            IOrganizationService service, Entity request, ITracingService trace)
        {
            var requestedBy = request.GetAttributeValue<string>("fmc_requestedby");
            if (IsEmail(requestedBy))
                return new Recipient(requestedBy.Trim(), null);

            Guid userId;
            if (!Guid.TryParse(requestedBy, out userId))
            {
                var createdBy = request.GetAttributeValue<EntityReference>("createdby");
                userId = createdBy == null ? Guid.Empty : createdBy.Id;
            }
            if (userId == Guid.Empty)
                return new Recipient(null, null);

            try
            {
                var user = service.Retrieve("systemuser", userId,
                    new ColumnSet("internalemailaddress", "fullname"));
                var email = user.GetAttributeValue<string>("internalemailaddress");
                return IsEmail(email)
                    ? new Recipient(email.Trim(), user.GetAttributeValue<string>("fullname"))
                    : new Recipient(null, user.GetAttributeValue<string>("fullname"));
            }
            catch (Exception ex)
            {
                trace.Trace("QueueSyncNotification could not resolve user email; UserId={0}; Error={1}",
                    userId, ex.GetType().Name);
                return new Recipient(null, null);
            }
        }

        private static Entity FindByCorrelation(IOrganizationService service, string correlationKey)
        {
            var query = new QueryExpression(NotificationTable)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };
            query.Criteria.AddCondition("fmc_correlationkey", ConditionOperator.Equal, correlationKey);
            var rows = service.RetrieveMultiple(query).Entities;
            return rows.Count == 0 ? null : rows[0];
        }

        private static bool IsTerminal(int status)
        {
            return status == Succeeded || status == CompletedWithIssues || status == Failed;
        }

        private static bool IsEmail(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 320 &&
                   value.Contains("@") && value.IndexOfAny(new[] { ' ', '\r', '\n' }) < 0;
        }

        private static int EventType(int status)
        {
            return status == Succeeded ? 789122000
                : status == CompletedWithIssues ? 789122001
                : 789122002;
        }

        private static string StatusLabel(int status)
        {
            return status == Succeeded ? "succeeded"
                : status == CompletedWithIssues ? "completed with issues"
                : "failed";
        }

        private static string Subject(int status)
        {
            return status == Succeeded ? "[FMC BMS] SQL sync succeeded"
                : status == CompletedWithIssues ? "[FMC BMS] SQL sync completed with issues"
                : "[FMC BMS] SQL sync failed";
        }

        private static string Body(Entity request, string statusLabel)
        {
            var requestName = Escape(request.GetAttributeValue<string>("fmc_name") ?? request.Id.ToString("D"));
            var delivered = request.GetAttributeValue<long?>("fmc_deliveredrows").GetValueOrDefault();
            var quarantined = request.GetAttributeValue<long?>("fmc_quarantinedrows").GetValueOrDefault();
            var pending = request.GetAttributeValue<long?>("fmc_pendingafter").GetValueOrDefault();
            var error = request.GetAttributeValue<string>("fmc_errormessage");
            var errorHtml = string.IsNullOrWhiteSpace(error)
                ? string.Empty
                : "<p><strong>Error:</strong> " + Escape(error) + "</p>";
            return "<h2>FMC BMS SQL synchronization " + Escape(statusLabel) + "</h2>" +
                   "<p>Request: <strong>" + requestName + "</strong></p>" +
                   "<ul><li>Delivered rows: " + delivered.ToString(CultureInfo.InvariantCulture) +
                   "</li><li>Quarantined rows: " + quarantined.ToString(CultureInfo.InvariantCulture) +
                   "</li><li>Pending rows after run: " + pending.ToString(CultureInfo.InvariantCulture) +
                   "</li></ul>" + errorHtml +
                   "<p>This message was generated automatically by FMCentralBms.</p>";
        }

        private static string Escape(string value)
        {
            return SecurityElement.Escape(value) ?? string.Empty;
        }

        private sealed class Recipient
        {
            public Recipient(string email, string name)
            {
                Email = email;
                Name = name;
            }

            public string Email { get; private set; }
            public string Name { get; private set; }
        }
    }
}
