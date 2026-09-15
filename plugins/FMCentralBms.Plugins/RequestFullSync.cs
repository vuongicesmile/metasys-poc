using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Publishes the unbound fmc_RequestFullSync business event. Power Automate
    /// dispatches the existing SQL and SharePoint Custom APIs independently.
    /// </summary>
    public sealed class RequestFullSync : IPlugin
    {
        private const string Message = "fmc_RequestFullSync";

        public void Execute(IServiceProvider serviceProvider)
        {
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            if (!string.Equals(context.MessageName, Message, StringComparison.Ordinal))
                return;

            var requestId = ReadClientRequestId(context) ?? Guid.NewGuid();
            trace.Trace("RequestFullSync START RequestId={0}; UserId={1}; CorrelationId={2}",
                requestId, context.InitiatingUserId, context.CorrelationId);

            context.OutputParameters["RequestId"] = requestId;
            context.OutputParameters["Accepted"] = true;
            context.OutputParameters["Message"] =
                "Full sync accepted. Power Automate will dispatch SQL and SharePoint sync requests.";

            trace.Trace("RequestFullSync ACCEPTED RequestId={0}", requestId);
        }

        private static Guid? ReadClientRequestId(IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("ClientRequestId") ||
                context.InputParameters["ClientRequestId"] == null)
                return null;

            var value = context.InputParameters["ClientRequestId"] as string;
            Guid parsed;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || !Guid.TryParse(value, out parsed))
                throw new InvalidPluginExecutionException(
                    "FULL-SYNC-001: ClientRequestId must be a GUID string when supplied.");
            return parsed;
        }
    }
}
