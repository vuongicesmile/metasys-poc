using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Shared parser for optional ClientRequestId Custom API inputs.
    /// </summary>
    internal static class ClientRequestId
    {
        public static Guid? ReadOptional(
            IPluginExecutionContext context,
            string invalidMessage = null)
        {
            if (context == null)
                throw new InvalidPluginExecutionException(nameof(context));

            if (!context.InputParameters.Contains("ClientRequestId") ||
                context.InputParameters["ClientRequestId"] == null)
                return null;

            var value = context.InputParameters["ClientRequestId"] as string;
            Guid parsed;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || !Guid.TryParse(value, out parsed))
                throw new InvalidPluginExecutionException(
                    invalidMessage ?? "ClientRequestId must be a GUID string when supplied.");
            return parsed;
        }
    }
}
