namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Publishes the unbound fmc_RequestSpoSync business event. Power Automate
    /// performs the SharePoint scan; this sandboxed plug-in never connects to SPO.
    /// </summary>
    public sealed class RequestSpoSync : BusinessEventPluginBase
    {
        // Plugin không gọi SharePoint trong sandbox; Power Automate dùng connector riêng.
        protected override string MessageName => "fmc_RequestSpoSync";
        protected override string AcceptedMessage =>
            "SharePoint scan accepted. Power Automate will archive only new or updated files.";

        protected override void AddOutputs(Microsoft.Xrm.Sdk.IPluginExecutionContext context)
        {
            // Empty scope preserves the existing full FMC-Inbox scan used by
            // the legacy web resource and the full-sync flow. A claim workflow
            // supplies all three values to archive exactly one stable version.
            CopyOptional(context, "SourceKey", 150);
            CopyOptional(context, "ExpectedETag", 200);
            CopyOptional(context, "SharePointIdentifier", 4000);
        }

        private static void CopyOptional(Microsoft.Xrm.Sdk.IPluginExecutionContext context, string name, int maximumLength)
        {
            if (!context.InputParameters.Contains(name) || context.InputParameters[name] == null)
                return;
            var value = context.InputParameters[name] as string;
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
                throw new Microsoft.Xrm.Sdk.InvalidPluginExecutionException(name + " is invalid.");
            context.OutputParameters[name] = value;
        }
    }
}
