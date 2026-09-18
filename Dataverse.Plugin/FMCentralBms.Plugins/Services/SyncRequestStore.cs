using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Dataverse I/O for fmc_syncrequest queueing used by RequestBmsSync.
    /// </summary>
    internal static class SyncRequestStore
    {
        public static Entity FindByCorrelation(IOrganizationService service, string correlationId)
        {
            var query = new QueryExpression(EntityNames.SyncRequest)
            {
                ColumnSet = new ColumnSet("fmc_status"),
                TopCount = 1
            };
            query.Criteria.AddCondition("fmc_correlationid", ConditionOperator.Equal, correlationId);
            return First(service.RetrieveMultiple(query));
        }

        public static Entity FindActive(IOrganizationService service, string pipeline)
        {
            var query = new QueryExpression(EntityNames.SyncRequest)
            {
                ColumnSet = new ColumnSet("fmc_status"),
                TopCount = 1
            };
            query.Criteria.AddCondition("fmc_pipeline", ConditionOperator.Equal, pipeline);
            query.Criteria.AddCondition("fmc_status", ConditionOperator.In,
                SyncRequestStatus.Queued, SyncRequestStatus.Running);
            query.Orders.Add(new OrderExpression("createdon", OrderType.Ascending));
            return First(service.RetrieveMultiple(query));
        }

        public static Entity CreateQueued(
            IOrganizationService service,
            string pipeline,
            string correlationId,
            string requestedBy)
        {
            var request = new Entity(EntityNames.SyncRequest)
            {
                ["fmc_name"] = "SQL Sync " + DateTime.UtcNow.ToString("O"),
                ["fmc_command"] = "DrainPending",
                ["fmc_pipeline"] = pipeline,
                ["fmc_activekey"] = pipeline,
                ["fmc_correlationid"] = correlationId,
                ["fmc_requestedby"] = requestedBy,
                ["fmc_status"] = new OptionSetValue(SyncRequestStatus.Queued)
            };
            request.Id = service.Create(request);
            request["fmc_status"] = new OptionSetValue(SyncRequestStatus.Queued);
            return request;
        }

        /// <summary>
        /// Creates a queued request, or returns the winner when two callers race
        /// on the active-key alternate key.
        /// </summary>
        public static Entity CreateQueuedOrReuseRaceWinner(
            IOrganizationService service,
            string pipeline,
            string correlationId,
            string requestedBy,
            out bool created)
        {
            try
            {
                created = true;
                return CreateQueued(service, pipeline, correlationId, requestedBy);
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                var winner = FindActive(service, pipeline);
                if (winner == null) throw;
                created = false;
                return winner;
            }
        }

        public static string ResolveRequestedBy(
            IOrganizationService service, Guid userId, ITracingService trace)
        {
            try
            {
                var user = service.Retrieve(EntityNames.SystemUser, userId,
                    new ColumnSet("internalemailaddress"));
                var email = user.GetAttributeValue<string>("internalemailaddress");
                if (EmailAddress.IsValid(email))
                    return email.Trim();
            }
            catch (Exception ex)
            {
                if (trace != null)
                    trace.Trace("SyncRequestStore could not resolve requester email; UserId={0}; Error={1}",
                        userId, ex.GetType().Name);
            }

            return userId.ToString("D");
        }

        public static void WriteResponse(
            IPluginExecutionContext context, Entity request, bool created, string message)
        {
            var status = request.GetAttributeValue<OptionSetValue>("fmc_status");
            context.OutputParameters["RequestId"] = request.Id;
            context.OutputParameters["Created"] = created;
            context.OutputParameters["Status"] = status == null
                ? SyncRequestStatus.Queued
                : status.Value;
            context.OutputParameters["Message"] = message;
        }

        private static Entity First(EntityCollection rows)
        {
            return rows.Entities.Count == 0 ? null : rows.Entities[0];
        }
    }
}
