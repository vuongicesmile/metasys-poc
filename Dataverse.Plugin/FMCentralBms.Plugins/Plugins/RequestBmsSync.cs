using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Implements the unbound fmc_RequestBmsSync action. The action only queues
    /// work in Dataverse; DataverseSyncWorker remains responsible for SQL access.
    /// </summary>
    public sealed class RequestBmsSync : IPlugin
    {
        private const string Message = "fmc_RequestBmsSync";

        public void Execute(IServiceProvider serviceProvider)
        {
            // Resolve tracing/context/factory một lần cho toàn bộ execution.
            ITracingService trace;
            IPluginExecutionContext context;
            IOrganizationServiceFactory factory;
            PluginServices.ResolveWithFactory(serviceProvider, out trace, out context, out factory);

            if (!string.Equals(context.MessageName, Message, StringComparison.Ordinal))
                return;

            // Request ID hợp lệ giúp retry từ UI idempotent; pipeline gắn với organization hiện tại.
            var clientRequestId = ClientRequestId.ReadOptional(
                context,
                "BMS-SYNC-001: ClientRequestId must be a GUID string when supplied.");
            var correlationId = (clientRequestId ?? Guid.NewGuid()).ToString("D");
            var pipeline = context.OrganizationId.ToString("D") + ":FMC";
            var service = PluginServices.CreateOrgService(factory, context);
            var requestedBy = SyncRequestStore.ResolveRequestedBy(service, context.InitiatingUserId, trace);

            if (trace != null)
                trace.Trace("RequestBmsSync START Pipeline={0}; HasClientRequestId={1}; CorrelationId={2}",
                    pipeline, clientRequestId.HasValue, correlationId);

            var existing = clientRequestId == null
                ? null
                : SyncRequestStore.FindByCorrelation(service, correlationId);
            if (existing != null)
            {
                // Cùng button click đã tạo request trước đó: trả request cũ, không tạo bản ghi mới.
                SyncRequestStore.WriteResponse(context, existing, false, "Reused request for this button click.");
                if (trace != null)
                    trace.Trace("RequestBmsSync REUSE_BY_ID RequestId={0}; CorrelationId={1}", existing.Id, correlationId);
                return;
            }

            existing = SyncRequestStore.FindActive(service, pipeline);
            if (existing != null)
            {
                // Chỉ cho phép một request Queued/Running cho mỗi pipeline tại một thời điểm.
                SyncRequestStore.WriteResponse(context, existing, false, "Reused the active sync request.");
                if (trace != null)
                    trace.Trace("RequestBmsSync REUSE_ACTIVE RequestId={0}; CorrelationId={1}", existing.Id, correlationId);
                return;
            }

            bool created;
            var request = SyncRequestStore.CreateQueuedOrReuseRaceWinner(
                service, pipeline, correlationId, requestedBy, out created);
            // Alternate key bảo vệ race giữa hai caller cùng lúc; winner được trả lại cho caller còn lại.
            SyncRequestStore.WriteResponse(
                context,
                request,
                created,
                created ? "Queued a new sync request." : "Reused the request created by another caller.");
            if (trace != null)
                trace.Trace(
                    created ? "RequestBmsSync CREATED RequestId={0}; CorrelationId={1}"
                            : "RequestBmsSync RACE_REUSE RequestId={0}; CorrelationId={1}",
                    request.Id, correlationId);
        }
    }
}
