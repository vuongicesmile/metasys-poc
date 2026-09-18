using System;
using System.Globalization;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FMCentralBms.Plugins
{
    internal sealed class NotificationRecipient
    {
        public NotificationRecipient(string email, string name)
        {
            Email = email;
            Name = name;
        }

        public string Email { get; private set; }
        public string Name { get; private set; }
    }

    /// <summary>
    /// Idempotent fmc_notification outbox writes for terminal sync requests.
    /// </summary>
    internal static class NotificationOutbox
    {
        public static string CorrelationKey(Guid syncRequestId, int statusValue)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "syncrequest:{0:N}:status:{1}", syncRequestId, statusValue);
        }

        public static Entity FindByCorrelation(IOrganizationService service, string correlationKey)
        {
            var query = new QueryExpression(EntityNames.Notification)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1
            };
            query.Criteria.AddCondition("fmc_correlationkey", ConditionOperator.Equal, correlationKey);
            var rows = service.RetrieveMultiple(query).Entities;
            return rows.Count == 0 ? null : rows[0];
        }

        public static Entity RetrieveSyncRequest(IOrganizationService service, Guid syncRequestId)
        {
            return service.Retrieve(EntityNames.SyncRequest, syncRequestId,
                new ColumnSet("fmc_name", "fmc_status", "fmc_requestedby", "fmc_deliveredrows",
                    "fmc_quarantinedrows", "fmc_pendingafter", "fmc_errormessage", "createdby"));
        }

        public static NotificationRecipient ResolveRecipient(
            IOrganizationService service, Entity request, ITracingService trace)
        {
            var requestedBy = request.GetAttributeValue<string>("fmc_requestedby");
            if (EmailAddress.IsValid(requestedBy))
                return new NotificationRecipient(requestedBy.Trim(), null);

            Guid userId;
            if (!Guid.TryParse(requestedBy, out userId))
            {
                var createdBy = request.GetAttributeValue<EntityReference>("createdby");
                userId = createdBy == null ? Guid.Empty : createdBy.Id;
            }
            if (userId == Guid.Empty)
                return new NotificationRecipient(null, null);

            try
            {
                var user = service.Retrieve(EntityNames.SystemUser, userId,
                    new ColumnSet("internalemailaddress", "fullname"));
                var email = user.GetAttributeValue<string>("internalemailaddress");
                return EmailAddress.IsValid(email)
                    ? new NotificationRecipient(email.Trim(), user.GetAttributeValue<string>("fullname"))
                    : new NotificationRecipient(null, user.GetAttributeValue<string>("fullname"));
            }
            catch (Exception ex)
            {
                if (trace != null)
                    trace.Trace("NotificationOutbox could not resolve user email; UserId={0}; Error={1}",
                        userId, ex.GetType().Name);
                return new NotificationRecipient(null, null);
            }
        }

        public static Guid CreateOrReuse(
            IOrganizationService service,
            Guid syncRequestId,
            int statusValue,
            Entity request,
            NotificationRecipient recipient,
            ITracingService trace)
        {
            var correlationKey = CorrelationKey(syncRequestId, statusValue);
            if (FindByCorrelation(service, correlationKey) != null)
            {
                if (trace != null)
                    trace.Trace("NotificationOutbox DUPLICATE CorrelationKey={0}", correlationKey);
                return Guid.Empty;
            }

            var label = SyncRequestStatus.Label(statusValue);
            var notification = new Entity(EntityNames.Notification)
            {
                ["fmc_name"] = "SQL sync " + label,
                ["fmc_channel"] = new OptionSetValue(NotificationChoices.EmailChannel),
                ["fmc_eventtype"] = new OptionSetValue(NotificationChoices.EventTypeForSyncStatus(statusValue)),
                ["fmc_status"] = new OptionSetValue(
                    recipient.Email == null ? NotificationChoices.Skipped : NotificationChoices.Pending),
                ["fmc_recipientemail"] = recipient.Email,
                ["fmc_recipientname"] = recipient.Name,
                ["fmc_subject"] = NotificationMessageBuilder.Subject(statusValue),
                ["fmc_body"] = NotificationMessageBuilder.Body(request, label),
                ["fmc_correlationkey"] = correlationKey,
                ["fmc_regardingtable"] = EntityNames.SyncRequest,
                ["fmc_regardingid"] = syncRequestId.ToString("D"),
                ["fmc_attempts"] = 0,
                ["fmc_errormessage"] = recipient.Email == null
                    ? "Notification skipped because no valid requester email was available."
                    : null
            };

            try
            {
                return service.Create(notification);
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                if (FindByCorrelation(service, correlationKey) != null)
                {
                    if (trace != null)
                        trace.Trace("NotificationOutbox RACE_REUSE CorrelationKey={0}", correlationKey);
                    return Guid.Empty;
                }
                throw;
            }
        }
    }
}
