using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Accept-and-ack handler for unbound Custom API business events.
    /// Power Automate / workers perform the real work outside the sandbox.
    /// </summary>
    public abstract class BusinessEventPluginBase : IPlugin
    {
        // Class con chỉ cần khai báo message và message phản hồi; phần nhận event
        // và trả response được dùng chung cho Full Sync/SPO Sync.
        protected abstract string MessageName { get; }
        protected abstract string AcceptedMessage { get; }

        public void Execute(IServiceProvider serviceProvider)
        {
            // Dataverse cung cấp context qua service provider của sandbox.
            ITracingService trace;
            IPluginExecutionContext context;
            PluginServices.Resolve(serviceProvider, out trace, out context);

            if (!string.Equals(context.MessageName, MessageName, StringComparison.Ordinal))
                return;

            // ClientRequestId giúp UI truy vết một lần bấm; nếu không có thì plugin tự tạo ID.
            var requestId = ClientRequestId.ReadOptional(context) ?? Guid.NewGuid();
            if (trace != null)
                trace.Trace("{0} START RequestId={1}; UserId={2}; CorrelationId={3}",
                    GetType().Name, requestId, context.InitiatingUserId, context.CorrelationId);

            // Plugin chỉ accept/queue event. Power Automate và worker mới thực hiện dữ liệu thật.
            context.OutputParameters["RequestId"] = requestId;
            context.OutputParameters["Accepted"] = true;
            context.OutputParameters["Message"] = AcceptedMessage;

            if (trace != null)
                trace.Trace("{0} ACCEPTED RequestId={1}", GetType().Name, requestId);
        }
    }
}
