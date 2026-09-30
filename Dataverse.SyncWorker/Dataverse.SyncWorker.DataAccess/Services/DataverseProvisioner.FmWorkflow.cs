using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace Dataverse.SyncWorker.DataAccess.Services;

public sealed partial class DataverseProvisioner
{
    private void ApplyMigration005(IOrganizationService service)
    {
        FmAttribute(service, S.Request, Time(S.OverdueOn, "Overdue On"));
        var actions = (PicklistAttributeMetadata)((RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest { EntityLogicalName = S.History, LogicalName = S.Action, RetrieveAsIfPublished = true })).AttributeMetadata;
        if (actions.OptionSet.Options.All(o => o.Value != S.OverdueHistory))
            service.Execute(new InsertOptionValueRequest { EntityLogicalName = S.History, AttributeLogicalName = S.Action, Value = S.OverdueHistory, Label = new Label("Overdue", 1033), SolutionUniqueName = Solution });
        foreach (var table in new[] { S.Request, S.History })
            service.Execute(new PublishXmlRequest { ParameterXml = $"<importexportxml><entities><entity>{table}</entity></entities></importexportxml>" });
    }

    private void ApplyMigration004(IOrganizationService service)
    {
        EnsureFmTable(service, S.Request, "FM Request", "FM Requests", OwnershipTypes.UserOwned);
        EnsureFmTable(service, S.Route, "Approval Route", "Approval Routes", OwnershipTypes.OrganizationOwned);
        EnsureFmTable(service, S.History, "Request History", "Request Histories", OwnershipTypes.OrganizationOwned);
        foreach (var field in new[] { S.Description, S.Mitigation, S.Plan }) FmAttribute(service, S.Request, FmMemo(field, field == S.Plan ? 50000 : 4000));
        foreach (var field in new[] { S.Department, S.Building, S.Requester, S.Approver }) FmAttribute(service, S.Request, Text(field, FmLabel(field), 320));
        foreach (var field in new[] { S.Due, S.SubmittedOn, S.CompletedOn, S.ReminderOn, S.EscalatedOn }) FmAttribute(service, S.Request, Time(field, FmLabel(field)));
        FmAttribute(service, S.Request, new DateTimeAttributeMetadata { SchemaName = S.ReviewDate, DisplayName = new Label("Review Date", 1033), Format = DateTimeFormat.DateOnly, DateTimeBehavior = DateTimeBehavior.DateOnly });
        foreach (var field in new[] { S.Step, S.Revision }) FmAttribute(service, S.Request, FmInteger(field, 0, int.MaxValue));
        foreach (var field in new[] { S.Likelihood, S.Impact }) FmAttribute(service, S.Request, FmInteger(field, 1, 5));
        FmAttribute(service, S.Request, FmDecimal(S.Value));
        FmAttribute(service, S.Request, FmChoice(S.Type, (S.Ciwg,"CIWG"), (S.Risk,"Risk"), (S.Project,"Project")));
        FmAttribute(service, S.Request, FmChoice(S.Severity, (S.Low,"Low"), (S.Medium,"Medium"), (S.High,"High"), (S.Critical,"Critical")));
        FmAttribute(service, S.Request, FmChoice(S.Status, (S.Draft,"Draft"), (S.Submitted,"Submitted"), (S.InApproval,"In Approval"), (S.Approved,"Approved"), (S.Rejected,"Rejected"), (S.Closed,"Closed")));
        // The existing .NET migration created a second status column; retain it for compatibility.
        var legacy = ((RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest { EntityLogicalName = S.Request, LogicalName = S.LegacyStatus, RetrieveAsIfPublished = true })).AttributeMetadata as PicklistAttributeMetadata;
        if (legacy?.OptionSet.Options.All(o => o.Value != 100000004) == true)
            service.Execute(new InsertOptionValueRequest { EntityLogicalName = S.Request, AttributeLogicalName = S.LegacyStatus, Value = 100000004, Label = new Label("Closed", 1033), SolutionUniqueName = Solution });

        foreach (var field in new[] { S.RouteApprover, S.Escalation, S.Department, S.Role }) FmAttribute(service, S.Route, Text(field, FmLabel(field), 320));
        foreach (var field in new[] { S.StepNo, S.Sla }) FmAttribute(service, S.Route, FmInteger(field, 1, 365));
        foreach (var field in new[] { S.ReminderHours, S.EscalationHours }) FmAttribute(service, S.Route, FmInteger(field, 0, 8760));
        FmAttribute(service, S.Route, FmDecimal(S.MinValue));
        FmAttribute(service, S.Route, FmChoice(S.Type, (S.Ciwg,"CIWG"), (S.Risk,"Risk"), (S.Project,"Project")));
        FmAttribute(service, S.Route, FmChoice(S.MinSeverity, (S.Low,"Low"), (S.Medium,"Medium"), (S.High,"High"), (S.Critical,"Critical")));
        FmAttribute(service, S.Route, new BooleanAttributeMetadata { SchemaName = S.Active, DisplayName = new Label("Enabled", 1033), DefaultValue = true, OptionSet = new BooleanOptionSetMetadata(new OptionMetadata(new Label("Yes",1033),1),new OptionMetadata(new Label("No",1033),0)) });

        foreach (var field in new[] { S.ActorEmail, S.ActorId, S.Command, S.FlowRun }) FmAttribute(service, S.History, Text(field, FmLabel(field), 320));
        FmAttribute(service, S.History, FmMemo(S.Comments, 4000));
        FmAttribute(service, S.History, Time(S.ActedOn, "Acted On"));
        foreach (var field in new[] { S.StepNo, S.ResultRevision, S.ResultStatus }) FmAttribute(service, S.History, FmInteger(field, 0, int.MaxValue));
        FmAttribute(service, S.History, FmChoice(S.Action, (S.SubmitHistory,"Submitted"), (S.ApproveHistory,"Approved"), (S.RejectHistory,"Rejected"), (S.RemindHistory,"Reminded"), (S.EscalateHistory,"Escalated"), (S.CloseHistory,"Closed")));
        if (!ColumnExists(service, S.History, S.RequestLookup))
            service.Execute(new CreateOneToManyRequest {
                SolutionUniqueName = Solution,
                OneToManyRelationship = new OneToManyRelationshipMetadata { SchemaName = "fmc_fmrequest_requesthistory_workflow", ReferencedEntity = S.Request, ReferencingEntity = S.History, CascadeConfiguration = new CascadeConfiguration { Delete = CascadeType.Restrict, Assign = CascadeType.NoCascade, Share = CascadeType.NoCascade, Unshare = CascadeType.NoCascade, Reparent = CascadeType.NoCascade, Merge = CascadeType.NoCascade } },
                Lookup = new LookupAttributeMetadata { SchemaName = S.RequestLookup, DisplayName = new Label("FM Request",1033) }
            });
        EnsureFmAppVariable(service);
        var events = (PicklistAttributeMetadata)((RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest { EntityLogicalName = "fmc_notification", LogicalName = "fmc_eventtype", RetrieveAsIfPublished = true })).AttributeMetadata;
        foreach (var item in new[] { (789122015, "Reminder"), (789122016, "Closed") })
            if (events.OptionSet.Options.All(o => o.Value != item.Item1))
                service.Execute(new InsertOptionValueRequest { EntityLogicalName = "fmc_notification", AttributeLogicalName = "fmc_eventtype", Value = item.Item1, Label = new Label(item.Item2, 1033), SolutionUniqueName = Solution });
        foreach (var table in new[] { S.Request, S.Route, S.History, "fmc_notification" })
            service.Execute(new PublishXmlRequest { ParameterXml = $"<importexportxml><entities><entity>{table}</entity></entities></importexportxml>" });
        Console.WriteLine("[migration 004] FM workflow schema, compatibility status and app configuration ready.");
    }

    private static void EnsureFmTable(IOrganizationService service, string table, string label, string plural, OwnershipTypes ownership)
    {
        if (!TableExists(service, table))
            service.Execute(new CreateEntityRequest { SolutionUniqueName = Solution,
                Entity = new EntityMetadata { SchemaName = table, DisplayName = new Label(label,1033), DisplayCollectionName = new Label(plural,1033), OwnershipType = ownership, IsActivity = false },
                PrimaryAttribute = Text(S.Name, "Name", 200) });
        var meta = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest { LogicalName = table, EntityFilters = EntityFilters.Entity })).EntityMetadata;
        if (meta.OwnershipType != ownership || meta.IsOptimisticConcurrencyEnabled != true)
            throw new InvalidOperationException($"Unexpected ownership/concurrency for {table}.");
        service.Execute(new AddSolutionComponentRequest { ComponentId = meta.MetadataId!.Value, ComponentType = 1, SolutionUniqueName = Solution, AddRequiredComponents = false });
    }

    private void FmAttribute(IOrganizationService service, string table, AttributeMetadata desired)
    {
        var logical = desired.SchemaName.ToLowerInvariant();
        if (!ColumnExists(service, table, logical))
            service.Execute(new CreateAttributeRequest { EntityName = table, SolutionUniqueName = Solution, Attribute = desired });
        else
        {
            var actual = ((RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest { EntityLogicalName = table, LogicalName = logical, RetrieveAsIfPublished = true })).AttributeMetadata;
            if (actual.GetType() != desired.GetType()) throw new InvalidOperationException($"Incompatible type: {table}.{logical}");
            if (desired is PicklistAttributeMetadata choice && actual is PicklistAttributeMetadata existing)
                foreach (var option in choice.OptionSet.Options)
                    if (!existing.OptionSet.Options.Any(o => o.Value == option.Value)) throw new InvalidOperationException($"Choice contract mismatch: {table}.{logical} {option.Value}");
        }
    }
    private static string FmLabel(string name) => name.Substring(4);
    private static MemoAttributeMetadata FmMemo(string field, int max) => new() { SchemaName = field, DisplayName = new Label(FmLabel(field),1033), MaxLength = max };
    private static IntegerAttributeMetadata FmInteger(string field, int min, int max) => new() { SchemaName = field, DisplayName = new Label(FmLabel(field),1033), MinValue = min, MaxValue = max };
    private static DecimalAttributeMetadata FmDecimal(string field) => new() { SchemaName = field, DisplayName = new Label(FmLabel(field),1033), MinValue = 0, MaxValue = 100000000000, Precision = 2 };
    private static PicklistAttributeMetadata FmChoice(string field, params (int value,string label)[] choices)
    {
        var options = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
        foreach (var (value,label) in choices) options.Options.Add(new OptionMetadata(new Label(label,1033), value));
        return new() { SchemaName = field, DisplayName = new Label(FmLabel(field),1033), OptionSet = options };
    }
    private void EnsureFmAppVariable(IOrganizationService service)
    {
        var q = new QueryExpression("environmentvariabledefinition") { ColumnSet = new ColumnSet("defaultvalue"), TopCount = 2 };
        q.Criteria.AddCondition("schemaname", ConditionOperator.Equal, "fmc_FmAppUrl");
        var rows = service.RetrieveMultiple(q).Entities;
        if (rows.Count > 1) throw new InvalidOperationException("Duplicate FM app URL environment variables.");
        if (rows.Count == 0)
        {
            var create = new CreateRequest { Target = new Entity("environmentvariabledefinition") {
                ["schemaname"] = "fmc_FmAppUrl", ["displayname"] = "FM Workflow App URL", ["type"] = new OptionSetValue(100000000),
                ["defaultvalue"] = options.Url.TrimEnd('/') + "/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89"
            }};
            create["SolutionUniqueName"] = Solution; service.Execute(create);
        }
        else if (string.IsNullOrWhiteSpace(rows[0].GetAttributeValue<string>("defaultvalue")))
            service.Update(new Entity("environmentvariabledefinition", rows[0].Id) {
                ["defaultvalue"] = options.Url.TrimEnd('/') + "/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89"
            });
    }
}
