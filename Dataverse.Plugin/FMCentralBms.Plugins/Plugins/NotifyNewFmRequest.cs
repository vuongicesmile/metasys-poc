using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Sends a native model-driven app notification to every active Operator after an FM Request is created.
    /// Register on asynchronous PostOperation so a notification failure cannot roll back the request.
    /// </summary>
    public sealed class NotifyNewFmRequest : IPlugin
    {
        private const string OperatorRole = "FMC BMS Demo Operator";

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService trace;
            IPluginExecutionContext context;
            IOrganizationServiceFactory factory;
            PluginServices.ResolveWithFactory(serviceProvider, out trace, out context, out factory);

            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(context.PrimaryEntityName, EntityNames.FmRequest, StringComparison.OrdinalIgnoreCase) ||
                context.PrimaryEntityId == Guid.Empty)
                return;

            // The creator need not have permission to enumerate roles or send notifications to other users.
            // The asynchronous step performs only these narrowly scoped reads and sends as Dataverse SYSTEM.
            var service = factory.CreateOrganizationService(null);
            var request = service.Retrieve(EntityNames.FmRequest, context.PrimaryEntityId,
                new ColumnSet("fmc_name"));
            var requestName = request.GetAttributeValue<string>("fmc_name") ?? "FM Request";
            var recipients = FindOperatorUsers(service);

            foreach (var userId in recipients)
            {
                var send = new OrganizationRequest("SendAppNotification");
                send["Title"] = "New FM Request";
                send["Body"] = requestName;
                send["Recipient"] = new EntityReference(EntityNames.SystemUser, userId);
                send["IconType"] = new OptionSetValue(100000000);
                send["ToastType"] = new OptionSetValue(200000000);
                send["OverrideContent"] = new Entity
                {
                    ["title"] = "[Open FM Request](?pagetype=entityrecord&etn=" +
                        EntityNames.FmRequest + "&id=" + context.PrimaryEntityId.ToString("D") + ")"
                };
                service.Execute(send);
            }

            if (trace != null)
                trace.Trace("NotifyNewFmRequest RequestId={0}; Recipients={1}",
                    context.PrimaryEntityId, recipients.Count);
        }

        private static HashSet<Guid> FindOperatorUsers(IOrganizationService service)
        {
            var users = new HashSet<Guid>();

            // Direct user -> role assignment.
            var direct = NewUserQuery();
            var userRoles = direct.AddLink("systemuserroles", "systemuserid", "systemuserid");
            var directRole = userRoles.AddLink("role", "roleid", "roleid");
            directRole.LinkCriteria.AddCondition("name", ConditionOperator.Equal, OperatorRole);
            AddUsers(service, direct, users);

            // A role can also be assigned to a team; include that team's active members.
            var viaTeam = NewUserQuery();
            var membership = viaTeam.AddLink("teammembership", "systemuserid", "systemuserid");
            var team = membership.AddLink("team", "teamid", "teamid");
            var teamRoles = team.AddLink("teamroles", "teamid", "teamid");
            var teamRole = teamRoles.AddLink("role", "roleid", "roleid");
            teamRole.LinkCriteria.AddCondition("name", ConditionOperator.Equal, OperatorRole);
            AddUsers(service, viaTeam, users);

            return users;
        }

        private static QueryExpression NewUserQuery()
        {
            var query = new QueryExpression(EntityNames.SystemUser)
            {
                ColumnSet = new ColumnSet("systemuserid"),
                Distinct = true,
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);
            return query;
        }

        private static void AddUsers(IOrganizationService service, QueryExpression query, HashSet<Guid> users)
        {
            while (true)
            {
                var page = service.RetrieveMultiple(query);
                foreach (var user in page.Entities)
                {
                    var id = user.GetAttributeValue<Guid>("systemuserid");
                    if (id != Guid.Empty) users.Add(id);
                }

                if (!page.MoreRecords) return;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }
    }
}
