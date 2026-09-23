using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>Atomically claims one pending SPO change for manual or timer dispatch.</summary>
    public sealed class ClaimSpoChange : IPlugin
    {
        private const string Message = "fmc_ClaimSpoChange";

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace;
            IPluginExecutionContext context;
            IOrganizationServiceFactory factory;
            PluginServices.ResolveWithFactory(serviceProvider, out trace, out context, out factory);
            if (!string.Equals(context.MessageName, Message, StringComparison.Ordinal)) return;
            if (!context.InputParameters.Contains("ChangeRequestId") || !(context.InputParameters["ChangeRequestId"] is Guid requestId) || requestId == Guid.Empty)
                throw new InvalidPluginExecutionException("ChangeRequestId must be a GUID.");

            var result = SpoChangeRequestStore.Claim(
                PluginServices.CreateOrgService(factory, context),
                requestId,
                SpoChangeRequestStore.RequiredText(context, "ExpectedETag", 200),
                SpoChangeRequestStore.OptionalText(context, "DispatchSource", 30, "Manual"),
                ClientRequestId.ReadOptional(context, "ClientRequestId must be a GUID string when supplied.") ?? Guid.NewGuid());
            context.OutputParameters["Accepted"] = result.Accepted;
            context.OutputParameters["Status"] = result.Status;
            context.OutputParameters["Message"] = result.Message;
            context.OutputParameters["RequestId"] = requestId;
            context.OutputParameters["DispatchId"] = result.DispatchId;
            context.OutputParameters["SourceKey"] = result.SourceKey;
            context.OutputParameters["ExpectedETag"] = result.ExpectedETag;
            context.OutputParameters["SharePointIdentifier"] = result.SharePointIdentifier;
            if (trace != null) trace.Trace("ClaimSpoChange Accepted={0}; RequestId={1}; Status={2}", result.Accepted, requestId, result.Status);
        }
    }
}
