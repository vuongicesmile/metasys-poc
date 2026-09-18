using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    public abstract class BusinessEventPluginBase : IPlugin
    {
        protected abstract string MessageName { get; }
        protected abstract string AcceptedMessage { get; }

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new InvalidPluginExecutionException(nameof(serviceProvider));

            var context = serviceProvider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            var trace = serviceProvider.GetService(typeof(ITracingService)) as ITracingService;
            if (context == null)
                throw new InvalidPluginExecutionException("Plugin execution context is required.");

            if (!string.Equals(context.MessageName, MessageName, StringComparison.Ordinal))
                return;

            var requestId = ClientRequestId.Read(context) ?? Guid.NewGuid();
            trace?.Trace("{0} START RequestId={1}; UserId={2}; CorrelationId={3}",
                GetType().Name, requestId, context.InitiatingUserId, context.CorrelationId);

            context.OutputParameters["RequestId"] = requestId;
            context.OutputParameters["Accepted"] = true;
            context.OutputParameters["Message"] = AcceptedMessage;

            trace?.Trace("{0} ACCEPTED RequestId={1}", GetType().Name, requestId);
        }
    }

    internal static class ClientRequestId
    {
        public static Guid? Read(IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("ClientRequestId") ||
                context.InputParameters["ClientRequestId"] == null)
                return null;

            var value = context.InputParameters["ClientRequestId"] as string;
            Guid parsed;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || !Guid.TryParse(value, out parsed))
                throw new InvalidPluginExecutionException(
                    "ClientRequestId must be a GUID string when supplied.");
            return parsed;
        }
    }
}