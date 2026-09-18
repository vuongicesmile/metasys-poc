namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Publishes the unbound fmc_RequestSpoSync business event. Power Automate
    /// performs the SharePoint scan; this sandboxed plug-in never connects to SPO.
    /// </summary>
    public sealed class RequestSpoSync : BusinessEventPluginBase
    {
        protected override string MessageName => "fmc_RequestSpoSync";
        protected override string AcceptedMessage =>
            "SharePoint scan accepted. Power Automate will archive only new or updated files.";
    }
}
