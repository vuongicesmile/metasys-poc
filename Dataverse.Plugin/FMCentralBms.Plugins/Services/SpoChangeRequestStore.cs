using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Durable, per-file debounce queue for SharePoint change notifications.
    /// It never contacts SharePoint: the flow owns that connector boundary.
    /// </summary>
    internal static class SpoChangeRequestStore
    {
        public const int Pending = 789112000;
        public const int Dispatching = 789112001;
        public const int Dispatched = 789112002;
        public const int Superseded = 789112003;
        public const int Failed = 789112004;
        public const int Imported = 789112005;
        private const int ConcurrencyVersionMismatch = -2147088254;

        public static Entity DetectOrUpdate(IOrganizationService service, SpoChangeInput input, out bool created)
        {
            var existing = FindActive(service, input.SourceKey);
            if (existing != null)
            {
                var update = new Entity(EntityNames.SpoChangeRequest, existing.Id)
                {
                    ["fmc_expectedetag"] = input.ExpectedETag,
                    ["fmc_sharepointidentifier"] = input.SharePointIdentifier,
                    ["fmc_sharepointpath"] = input.SharePointPath,
                    ["fmc_filename"] = input.FileName,
                    ["fmc_libraryid"] = input.LibraryId,
                    ["fmc_itemid"] = input.ItemId,
                    ["fmc_name"] = Name(input.FileName),
                    ["fmc_errormessage"] = null
                };
                service.Update(update);
                existing.Attributes["fmc_expectedetag"] = input.ExpectedETag;
                created = false;
                return existing;
            }

            var request = NewPending(input);
            try
            {
                request.Id = service.Create(request);
                created = true;
                return request;
            }
            catch (FaultException<OrganizationServiceFault>)
            {
                var winner = FindActive(service, input.SourceKey);
                if (winner == null) throw;
                // The winner can have a previous ETag from a concurrent flow;
                // updating it retains its first DueAt rather than postponing it.
                service.Update(new Entity(EntityNames.SpoChangeRequest, winner.Id)
                {
                    ["fmc_expectedetag"] = input.ExpectedETag,
                    ["fmc_sharepointidentifier"] = input.SharePointIdentifier,
                    ["fmc_sharepointpath"] = input.SharePointPath,
                    ["fmc_filename"] = input.FileName,
                    ["fmc_libraryid"] = input.LibraryId,
                    ["fmc_itemid"] = input.ItemId,
                    ["fmc_name"] = Name(input.FileName)
                });
                winner.Attributes["fmc_expectedetag"] = input.ExpectedETag;
                created = false;
                return winner;
            }
        }

        public static SpoClaimResult Claim(IOrganizationService service, Guid requestId, string expectedETag,
            string dispatchSource, Guid dispatchId)
        {
            var row = service.Retrieve(EntityNames.SpoChangeRequest, requestId, new ColumnSet(
                "fmc_sourcekey", "fmc_expectedetag", "fmc_sharepointidentifier", "fmc_status", "versionnumber"));
            var status = row.GetAttributeValue<OptionSetValue>("fmc_status")?.Value ?? Pending;
            var currentETag = row.GetAttributeValue<string>("fmc_expectedetag") ?? "";
            if (!string.Equals(currentETag, expectedETag, StringComparison.Ordinal))
                return SpoClaimResult.Rejected(row, "Superseded", "A newer SharePoint version is waiting.");
            if (status != Pending)
                return SpoClaimResult.Rejected(row, StatusName(status), "This change was already claimed.");

            var update = new Entity(EntityNames.SpoChangeRequest, requestId) { RowVersion = row.RowVersion };
            update["fmc_status"] = new OptionSetValue(Dispatched);
            update["fmc_activekey"] = null;
            update["fmc_dispatchsource"] = dispatchSource;
            update["fmc_dispatchedat"] = DateTime.UtcNow;
            update["fmc_correlationkey"] = dispatchId.ToString("D");
            update["fmc_errormessage"] = null;
            try
            {
                service.Execute(new UpdateRequest
                {
                    Target = update,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches
                });
                return SpoClaimResult.Granted(row, dispatchId);
            }
            catch (FaultException<OrganizationServiceFault> fault)
                when (fault.Detail.ErrorCode == ConcurrencyVersionMismatch)
            {
                return SpoClaimResult.Rejected(row, "Already claimed", "Another caller claimed this change first.");
            }
        }

        private static Entity FindActive(IOrganizationService service, string sourceKey)
        {
            var query = new QueryExpression(EntityNames.SpoChangeRequest)
            {
                ColumnSet = new ColumnSet("fmc_expectedetag", "fmc_dueat", "fmc_status"),
                TopCount = 2
            };
            query.Criteria.AddCondition("fmc_activekey", ConditionOperator.Equal, sourceKey);
            var rows = service.RetrieveMultiple(query).Entities;
            if (rows.Count > 1)
                throw new InvalidPluginExecutionException("SPO-CHANGE-001: duplicate active source key.");
            return rows.Count == 0 ? null : rows[0];
        }

        private static Entity NewPending(SpoChangeInput input)
        {
            var now = DateTime.UtcNow;
            return new Entity(EntityNames.SpoChangeRequest)
            {
                ["fmc_name"] = Name(input.FileName),
                ["fmc_sourcekey"] = input.SourceKey,
                ["fmc_activekey"] = input.SourceKey,
                ["fmc_libraryid"] = input.LibraryId,
                ["fmc_itemid"] = input.ItemId,
                ["fmc_sharepointidentifier"] = input.SharePointIdentifier,
                ["fmc_sharepointpath"] = input.SharePointPath,
                ["fmc_filename"] = input.FileName,
                ["fmc_expectedetag"] = input.ExpectedETag,
                ["fmc_detectedat"] = now,
                ["fmc_dueat"] = now.AddMinutes(5),
                ["fmc_status"] = new OptionSetValue(Pending)
            };
        }

        private static string Name(string fileName) => ("SPO change: " + fileName).Substring(0, Math.Min(200, 12 + fileName.Length));

        public static string RequiredText(IPluginExecutionContext context, string name, int maximumLength)
        {
            var value = context.InputParameters.Contains(name) ? context.InputParameters[name] as string : null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
                throw new InvalidPluginExecutionException(name + " is required and exceeds its supported length.");
            return value.Trim();
        }

        public static string OptionalText(IPluginExecutionContext context, string name, int maximumLength, string defaultValue)
        {
            if (!context.InputParameters.Contains(name) || context.InputParameters[name] == null) return defaultValue;
            var value = context.InputParameters[name] as string;
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
                throw new InvalidPluginExecutionException(name + " is invalid.");
            return value.Trim();
        }

        private static string StatusName(int status)
        {
            switch (status)
            {
                case Pending: return "Pending";
                case Dispatching: return "Dispatching";
                case Dispatched: return "Dispatched";
                case Superseded: return "Superseded";
                case Failed: return "Failed";
                case Imported: return "Imported";
                default: return "Unknown";
            }
        }
    }

    internal sealed class SpoChangeInput
    {
        public string SourceKey;
        public string LibraryId;
        public string ItemId;
        public string SharePointIdentifier;
        public string SharePointPath;
        public string FileName;
        public string ExpectedETag;
    }

    internal sealed class SpoClaimResult
    {
        public bool Accepted;
        public string Status;
        public string Message;
        public Guid DispatchId;
        public string SourceKey;
        public string ExpectedETag;
        public string SharePointIdentifier;

        public static SpoClaimResult Granted(Entity row, Guid dispatchId) => new SpoClaimResult
        {
            Accepted = true,
            Status = "Dispatched",
            Message = "SharePoint change claimed for dispatch.",
            DispatchId = dispatchId,
            SourceKey = row.GetAttributeValue<string>("fmc_sourcekey"),
            ExpectedETag = row.GetAttributeValue<string>("fmc_expectedetag"),
            SharePointIdentifier = row.GetAttributeValue<string>("fmc_sharepointidentifier")
        };

        public static SpoClaimResult Rejected(Entity row, string status, string message) => new SpoClaimResult
        {
            Accepted = false,
            Status = status,
            Message = message,
            SourceKey = row.GetAttributeValue<string>("fmc_sourcekey"),
            ExpectedETag = row.GetAttributeValue<string>("fmc_expectedetag"),
            SharePointIdentifier = row.GetAttributeValue<string>("fmc_sharepointidentifier")
        };
    }
}
