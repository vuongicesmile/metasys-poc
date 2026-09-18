using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Ensures every Equipment create or explicit Building lookup update has a Building.
    /// </summary>
    public sealed class RequireEquipmentBuilding : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace;
            IPluginExecutionContext context;
            PluginServices.Resolve(serviceProvider, out trace, out context);

            if (trace != null)
                trace.Trace("RequireEquipmentBuilding START Message={0}; Stage={1}; Mode={2}; Depth={3}; CorrelationId={4}; OperationId={5}",
                    context.MessageName, context.Stage, context.Mode, context.Depth, context.CorrelationId, context.OperationId);

            var isCreate = string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase);
            var isUpdate = string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase);

            if ((!isCreate && !isUpdate) || context.Stage != 10 || context.Mode != 0)
            {
                if (trace != null)
                    trace.Trace("RequireEquipmentBuilding SKIP: unsupported message, stage or mode.");
                return;
            }

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity) ||
                ((Entity)context.InputParameters["Target"]).LogicalName != EntityNames.Equipment)
            {
                if (trace != null)
                    trace.Trace("RequireEquipmentBuilding SKIP: missing/invalid Target or different table.");
                return;
            }

            var target = (Entity)context.InputParameters["Target"];

            // Update payloads contain only the columns sent by the caller. An omitted
            // lookup means "leave it unchanged"; an explicit null clears the lookup.
            if (isUpdate && !target.Contains("fmc_buildingid"))
            {
                if (trace != null)
                    trace.Trace("RequireEquipmentBuilding SKIP: Update omitted fmc_buildingid; lookup unchanged.");
                return;
            }

            if (target.GetAttributeValue<EntityReference>("fmc_buildingid") == null)
            {
                if (trace != null)
                    trace.Trace("RequireEquipmentBuilding BLOCK BMS-EQUIPMENT-001: Building missing/null; LookupIncluded={0}; CorrelationId={1}",
                        target.Contains("fmc_buildingid"), context.CorrelationId);

                throw new InvalidPluginExecutionException(
                    "BMS-EQUIPMENT-001: Equipment phai thuoc mot Building. " +
                    "Hay chon Building truoc khi luu.");
            }

            if (trace != null)
                trace.Trace("RequireEquipmentBuilding PASS: Building supplied; validation completed.");
        }
    }
}
