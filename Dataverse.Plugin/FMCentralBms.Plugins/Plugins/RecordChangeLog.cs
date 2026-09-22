#if NET10_0_OR_GREATER
#nullable disable
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FMCentralBms.Plugins
{
    /// <summary>PostOperation synchronous business change log; participates in the source transaction.</summary>
    public sealed class RecordChangeLog : IPlugin
    {
        public static readonly string[] BuildingColumns =
            { "fmc_name", "fmc_buildingcode", "fmc_sourcebuilding", "fmc_description", "statecode", "statuscode" };
        public static readonly string[] EquipmentColumns =
            { "fmc_name", "fmc_equipmentcode", "fmc_equipmenttype", "fmc_buildingid", "fmc_description", "statecode", "statuscode" };

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace;
            IPluginExecutionContext context;
            IOrganizationServiceFactory factory;
            PluginServices.ResolveWithFactory(serviceProvider, out trace, out context, out factory);
            var columns = Columns(context.PrimaryEntityName);
            if (columns == null || context.Stage != 40 || context.Mode != 0 ||
                !(context.MessageName == "Create" || context.MessageName == "Update" || context.MessageName == "Delete")) return;

            // Do not skip Depth > 1: API/worker and nested writes need the same log coverage.
            Entity before = null, after = null;
            if (context.MessageName != "Create")
            {
                if (!context.PreEntityImages.Contains("Before"))
                    throw new InvalidPluginExecutionException("BMS-LOG-001: Missing Before image; contact the administrator.");
                before = context.PreEntityImages["Before"];
            }
            if (context.MessageName != "Delete")
            {
                if (!context.PostEntityImages.Contains("After"))
                    throw new InvalidPluginExecutionException("BMS-LOG-002: Missing After image; contact the administrator.");
                after = context.PostEntityImages["After"];
            }
            var log = BuildLog(context.PrimaryEntityName, context.PrimaryEntityId, context.MessageName,
                before, after, context.InitiatingUserId, context.UserId, context.CorrelationId, DateTime.UtcNow);
            if (log == null) return; // Sending the existing value is not a business change.
            var service = factory.CreateOrganizationService(context.UserId);
            var actor = service.Retrieve("systemuser", context.InitiatingUserId, new ColumnSet("fullname"));
            log["fmc_username"] = actor.GetAttributeValue<string>("fullname") ?? context.InitiatingUserId.ToString("D");
            // Failure stays visible and rolls back the source operation, never silently loses a log.
            service.Create(log);
            if (trace != null) trace.Trace("Change log: {0}/{1}/{2}", context.PrimaryEntityName, context.PrimaryEntityId, context.MessageName);
        }

        public static string[] Columns(string table)
        {
            if (table == "fmc_bmsbuilding") return BuildingColumns;
            if (table == "fmc_bmsequipment") return EquipmentColumns;
            return null;
        }

        public static Entity BuildLog(string table, Guid id, string operation, Entity before, Entity after,
            Guid actor, Guid runAs, Guid correlation, DateTime utcNow)
        {
            var columns = Columns(table) ?? throw new ArgumentException("Unsupported log table.");
            var changed = columns.Where(c => Value(before, c) != Value(after, c)).ToArray();
            if (operation == "Update" && changed.Length == 0) return null;
            var recordName = (after ?? before).GetAttributeValue<string>("fmc_name") ?? id.ToString("D");
            return new Entity("fmc_changelog")
            {
                ["fmc_name"] = (operation + ": " + recordName).Substring(0, Math.Min(200, operation.Length + 2 + recordName.Length)),
                ["fmc_operation"] = operation,
                ["fmc_tablename"] = table,
                ["fmc_recordid"] = id.ToString("D"),
                ["fmc_recordname"] = recordName,
                ["fmc_userid"] = actor.ToString("D"),
                ["fmc_runasuserid"] = runAs.ToString("D"),
                ["fmc_occurredon"] = utcNow,
                ["fmc_correlationid"] = correlation.ToString("D"),
                ["fmc_changedfields"] = string.Join(", ", changed),
                ["fmc_before"] = Snapshot(before, columns),
                ["fmc_after"] = Snapshot(after, columns)
            };
        }

        private static string Value(Entity entity, string column)
        {
            object value;
            if (entity == null || !entity.Attributes.TryGetValue(column, out value) || value == null) return null;
            var reference = value as EntityReference;
            if (reference != null) return reference.LogicalName + ":" + reference.Id.ToString("D");
            var choice = value as OptionSetValue;
            if (choice != null) return choice.Value.ToString(CultureInfo.InvariantCulture);
            var money = value as Money;
            if (money != null) return money.Value.ToString(CultureInfo.InvariantCulture);
            if (value is DateTime) return ((DateTime)value).ToUniversalTime().ToString("O");
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string Snapshot(Entity entity, IEnumerable<string> columns) => entity == null ? "null" :
            "{" + string.Join(",", columns.Select(c => Quote(c) + ":" + Quote(Value(entity, c)))) + "}";

        private static string Quote(string value)
        {
            if (value == null) return "null";
            var result = new StringBuilder("\"");
            foreach (var c in value)
            {
                if (c == '\\' || c == '"') result.Append('\\').Append(c);
                else if (c < 32) result.Append("\\u").Append(((int)c).ToString("x4"));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }
    }
}
