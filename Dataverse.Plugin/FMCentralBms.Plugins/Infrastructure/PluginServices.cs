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
            // Không đoán dependency; thiếu context là lỗi cấu hình plugin registration.
            if (serviceProvider == null)
                throw new InvalidPluginExecutionException(nameof(serviceProvider));

            trace = serviceProvider.GetService(typeof(ITracingService)) as ITracingService;
            // Trace có thể null ở một số execution context nên caller luôn kiểm tra trước khi ghi.
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
            // Dùng UserId của context để Dataverse áp dụng đúng quyền của caller/plugin step.
            return factory.CreateOrganizationService(context.UserId);
        }
    }
}
