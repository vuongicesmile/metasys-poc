using System;
using System.Globalization;
using System.Security;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Builds fmc_notification subject/body text for SQL sync terminal states.
    /// </summary>
    internal static class NotificationMessageBuilder
    {
        public static string Subject(int syncStatus)
        {
            return syncStatus == SyncRequestStatus.Succeeded ? "[FMC BMS] SQL sync succeeded"
                : syncStatus == SyncRequestStatus.CompletedWithIssues
                    ? "[FMC BMS] SQL sync completed with issues"
                    : "[FMC BMS] SQL sync failed";
        }

        public static string Body(Entity request, string statusLabel)
        {
            var requestName = Escape(request.GetAttributeValue<string>("fmc_name") ?? request.Id.ToString("D"));
            var delivered = request.GetAttributeValue<long?>("fmc_deliveredrows").GetValueOrDefault();
            var quarantined = request.GetAttributeValue<long?>("fmc_quarantinedrows").GetValueOrDefault();
            var pending = request.GetAttributeValue<long?>("fmc_pendingafter").GetValueOrDefault();
            var error = request.GetAttributeValue<string>("fmc_errormessage");
            var errorHtml = string.IsNullOrWhiteSpace(error)
                ? string.Empty
                : "<p><strong>Error:</strong> " + Escape(error) + "</p>";
            return "<h2>FMC BMS SQL synchronization " + Escape(statusLabel) + "</h2>" +
                   "<p>Request: <strong>" + requestName + "</strong></p>" +
                   "<ul><li>Delivered rows: " + delivered.ToString(CultureInfo.InvariantCulture) +
                   "</li><li>Quarantined rows: " + quarantined.ToString(CultureInfo.InvariantCulture) +
                   "</li><li>Pending rows after run: " + pending.ToString(CultureInfo.InvariantCulture) +
                   "</li></ul>" + errorHtml +
                   "<p>This message was generated automatically by FMCentralBms.</p>";
        }

        public static string Escape(string value)
        {
            return SecurityElement.Escape(value) ?? string.Empty;
        }
    }
}
