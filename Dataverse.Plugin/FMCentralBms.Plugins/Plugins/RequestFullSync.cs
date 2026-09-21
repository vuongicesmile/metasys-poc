namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Publishes the unbound fmc_RequestFullSync business event. Power Automate
    /// dispatches the existing SQL and SharePoint Custom APIs independently.
    /// </summary>
    public sealed class RequestFullSync : BusinessEventPluginBase
    {
        // Plugin chỉ trả Accepted; flow bên ngoài chịu trách nhiệm gọi hai API con.
        protected override string MessageName => "fmc_RequestFullSync";
        protected override string AcceptedMessage =>
            "Full sync accepted. Power Automate will dispatch SQL and SharePoint sync requests.";
    }
}
