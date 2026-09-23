using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>Records a debounced SPO file-version notification from the SharePoint flow.</summary>
    public sealed class DetectSpoChange : IPlugin
    {
        private const string Message = "fmc_DetectSpoChange";

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace;
            IPluginExecutionContext context;
            IOrganizationServiceFactory factory;
            PluginServices.ResolveWithFactory(serviceProvider, out trace, out context, out factory);
            if (!string.Equals(context.MessageName, Message, StringComparison.Ordinal)) return;

            var input = new SpoChangeInput
            {
                SourceKey = SpoChangeRequestStore.RequiredText(context, "SourceKey", 150),
                LibraryId = SpoChangeRequestStore.RequiredText(context, "LibraryId", 200),
                ItemId = SpoChangeRequestStore.RequiredText(context, "ItemId", 100),
                SharePointIdentifier = SpoChangeRequestStore.RequiredText(context, "SharePointIdentifier", 4000),
                SharePointPath = SpoChangeRequestStore.RequiredText(context, "SharePointPath", 4000),
                FileName = SpoChangeRequestStore.RequiredText(context, "FileName", 255),
                ExpectedETag = SpoChangeRequestStore.RequiredText(context, "ExpectedETag", 200)
            };
            var request = SpoChangeRequestStore.DetectOrUpdate(PluginServices.CreateOrgService(factory, context), input, out var created);
            context.OutputParameters["RequestId"] = request.Id;
            context.OutputParameters["Created"] = created;
            context.OutputParameters["Status"] = "Pending";
            context.OutputParameters["DueAt"] = request.GetAttributeValue<DateTime>("fmc_dueat");
            context.OutputParameters["Message"] = created ? "SharePoint change is waiting for its five-minute debounce window." : "Updated the pending SharePoint change without extending its due time.";
            if (trace != null) trace.Trace("DetectSpoChange {0}; RequestId={1}; SourceKey={2}", created ? "CREATED" : "UPDATED", request.Id, input.SourceKey);
        }
    }
}
