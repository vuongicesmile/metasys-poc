using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Resolves the CRM services every plug-in needs from the Dataverse service provider.
    /// </summary>
    internal static class PluginServices
    {
        public static void Resolve(
            IServiceProvider serviceProvider,
            out ITracingService trace,
            out IPluginExecutionContext context)
        {
            if (serviceProvider == null)
                throw new InvalidPluginExecutionException(nameof(serviceProvider));

            trace = serviceProvider.GetService(typeof(ITracingService)) as ITracingService;
            context = serviceProvider.GetService(typeof(IPluginExecutionContext)) as IPluginExecutionContext;
            if (context == null)
                throw new InvalidPluginExecutionException("Plugin execution context is required.");
        }

        public static void ResolveWithFactory(
            IServiceProvider serviceProvider,
            out ITracingService trace,
            out IPluginExecutionContext context,
            out IOrganizationServiceFactory factory)
        {
            Resolve(serviceProvider, out trace, out context);
            factory = serviceProvider.GetService(typeof(IOrganizationServiceFactory)) as IOrganizationServiceFactory;
            if (factory == null)
                throw new InvalidPluginExecutionException("Organization service factory is required.");
        }

        public static IOrganizationService CreateOrgService(
            IOrganizationServiceFactory factory,
            IPluginExecutionContext context)
        {
            return factory.CreateOrganizationService(context.UserId);
        }
    }
}
