using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Implements the unbound fmc_RequestBmsSync action. The action only queues
    /// work in Dataverse; DataverseSyncWorker remains responsible for SQL access.
    /// </summary>
    public sealed class RequestBmsSync : IPlugin
    {
        private const string Message = "fmc_RequestBmsSync";
        private const string RequestTable = "fmc_syncrequest";
        private const int Queued = 789100000;
        private const int Running = 789100001;

        public void Execute(IServiceProvider serviceProvider)
        {
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            if (!string.Equals(context.MessageName, Message, StringComparison.Ordinal))
                return;

            var clientRequestId = ReadClientRequestId(context);
            var correlationId = clientRequestId ?? Guid.NewGuid().ToString("D");
            var pipeline = context.OrganizationId.ToString("D") + ":FMC";
            var service = factory.CreateOrganizationService(context.UserId);

            trace.Trace("RequestBmsSync START Pipeline={0}; HasClientRequestId={1}; CorrelationId={2}",
                pipeline, clientRequestId != null, correlationId);

            var existing = clientRequestId == null ? null : FindByCorrelation(service, correlationId);
            if (existing != null)
            {
                Respond(context, existing, false, "Reused request for this button click.");
                trace.Trace("RequestBmsSync REUSE_BY_ID RequestId={0}; CorrelationId={1}", existing.Id, correlationId);
                return;
            }

            existing = FindActive(service, pipeline);
            if (existing != null)
            {
                Respond(context, existing, false, "Reused the active sync request.");
                trace.Trace("RequestBmsSync REUSE_ACTIVE RequestId={0}; CorrelationId={1}", existing.Id, correlationId);
                return;
            }

            try
            {
                var request = new Entity(RequestTable)
                {
                    ["fmc_name"] = "SQL Sync " + DateTime.UtcNow.ToString("O"),
                    ["fmc_command"] = "DrainPending",
                    ["fmc_pipeline"] = pipeline,
                    ["fmc_activekey"] = pipeline,
                    ["fmc_correlationid"] = correlationId,
                    ["fmc_requestedby"] = context.InitiatingUserId.ToString("D"),
                    ["fmc_status"] = new OptionSetValue(Queued)
                };
                request.Id = service.Create(request);
                request["fmc_status"] = new OptionSetValue(Queued);
                Respond(context, request, true, "Queued a new sync request.");
                trace.Trace("RequestBmsSync CREATED RequestId={0}; CorrelationId={1}", request.Id, correlationId);
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                // Two callers can both observe an empty queue. The active-key
                // alternate key chooses the winner; the other caller reuses it.
                var winner = FindActive(service, pipeline);
                if (winner == null) throw;
                Respond(context, winner, false, "Reused the request created by another caller.");
                trace.Trace("RequestBmsSync RACE_REUSE RequestId={0}; CorrelationId={1}", winner.Id, correlationId);
            }
        }

        private static string ReadClientRequestId(IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("ClientRequestId") ||
                context.InputParameters["ClientRequestId"] == null)
                return null;

            var value = context.InputParameters["ClientRequestId"] as string;
            Guid parsed;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || !Guid.TryParse(value, out parsed))
                throw new InvalidPluginExecutionException(
                    "BMS-SYNC-001: ClientRequestId must be a GUID string when supplied.");
            return parsed.ToString("D");
        }

        private static Entity FindByCorrelation(IOrganizationService service, string correlationId)
        {
            var query = new QueryExpression(RequestTable)
            {
                ColumnSet = new ColumnSet("fmc_status"),
                TopCount = 1
            };
            query.Criteria.AddCondition("fmc_correlationid", ConditionOperator.Equal, correlationId);
            return First(service.RetrieveMultiple(query));
        }

        private static Entity FindActive(IOrganizationService service, string pipeline)
        {
            var query = new QueryExpression(RequestTable)
            {
                ColumnSet = new ColumnSet("fmc_status"),
                TopCount = 1
            };
            query.Criteria.AddCondition("fmc_pipeline", ConditionOperator.Equal, pipeline);
            query.Criteria.AddCondition("fmc_status", ConditionOperator.In, Queued, Running);
            query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));
            return First(service.RetrieveMultiple(query));
        }

        private static Entity First(EntityCollection rows)
        {
            return rows.Entities.Count == 0 ? null : rows.Entities[0];
        }

        private static void Respond(IPluginExecutionContext context, Entity request, bool created, string message)
        {
            var status = request.GetAttributeValue<OptionSetValue>("fmc_status");
            context.OutputParameters["RequestId"] = request.Id;
            context.OutputParameters["Created"] = created;
            context.OutputParameters["Status"] = status == null ? Queued : status.Value;
            context.OutputParameters["Message"] = message;
        }
    }
}
