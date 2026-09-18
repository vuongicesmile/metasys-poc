using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Creates an idempotent notification outbox record when a SQL sync request
    /// reaches a terminal state. Email delivery is handled asynchronously by
    /// Power Automate and never participates in the sync transaction.
    /// </summary>
    public sealed class QueueSyncNotification : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace;
            IPluginExecutionContext context;
            IOrganizationServiceFactory factory;
            PluginServices.ResolveWithFactory(serviceProvider, out trace, out context, out factory);

            if (!string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(context.PrimaryEntityName, EntityNames.SyncRequest, StringComparison.OrdinalIgnoreCase) ||
                context.PrimaryEntityId == Guid.Empty)
                return;

            var target = context.InputParameters.Contains("Target")
                ? context.InputParameters["Target"] as Entity
                : null;
            var status = target == null
                ? null
                : target.GetAttributeValue<OptionSetValue>("fmc_status");
            if (status == null || !SyncRequestStatus.IsTerminal(status.Value))
                return;

            var service = PluginServices.CreateOrgService(factory, context);
            var request = NotificationOutbox.RetrieveSyncRequest(service, context.PrimaryEntityId);
            var recipient = NotificationOutbox.ResolveRecipient(service, request, trace);
            var notificationId = NotificationOutbox.CreateOrReuse(
                service, context.PrimaryEntityId, status.Value, request, recipient, trace);

            if (notificationId != Guid.Empty && trace != null)
                trace.Trace("QueueSyncNotification CREATED NotificationId={0}; Status={1}; HasRecipient={2}",
                    notificationId, status.Value, recipient.Email != null);
        }
    }
}
