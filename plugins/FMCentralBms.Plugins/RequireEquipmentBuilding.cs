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
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            bool isCreate = string.Equals(context.MessageName, "Create",
                StringComparison.OrdinalIgnoreCase);
            bool isUpdate = string.Equals(context.MessageName, "Update",
                StringComparison.OrdinalIgnoreCase);

            if ((!isCreate && !isUpdate) || context.Stage != 10 || context.Mode != 0)
                return;

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != "fmc_bmsequipment")
                return;

            // Update payloads contain only the columns sent by the caller. An omitted
            // lookup means "leave it unchanged"; an explicit null clears the lookup.
            if (isUpdate && !target.Contains("fmc_buildingid"))
                return;

            if (target.GetAttributeValue<EntityReference>("fmc_buildingid") == null)
            {
                var trace = (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));
                trace?.Trace("RequireEquipmentBuilding blocked {0}; CorrelationId={1}",
                    context.MessageName, context.CorrelationId);

                throw new InvalidPluginExecutionException(
                    "BMS-EQUIPMENT-001: Equipment phai thuoc mot Building. " +
                    "Hay chon Building truoc khi luu.");
            }
        }
    }
}
