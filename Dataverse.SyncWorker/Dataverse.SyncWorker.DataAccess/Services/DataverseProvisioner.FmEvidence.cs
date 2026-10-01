using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace Dataverse.SyncWorker.DataAccess.Services;

public sealed partial class DataverseProvisioner
{
    private void ApplyMigration006(IOrganizationService service)
    {
        if (!TableExists(service, S.ReadingTable))
            throw new InvalidOperationException("Standard reading table must exist before FM evidence provisioning.");
        FmAttribute(service, S.Request, new MemoAttributeMetadata {
            SchemaName = S.EvidenceSnapshot, DisplayName = new Label("Reading evidence snapshot", 1033), MaxLength = 12000 });
        // A frozen source reference needs Read on the reading, not Append To on telemetry.
        // The server validates this GUID and captures the source; it is not a client-authored claim.
        FmAttribute(service, S.Request, Text(S.EvidenceReading, "Evidence reading ID", 36));
        var table = ((RetrieveEntityResponse)service.Execute(new RetrieveEntityRequest {
            LogicalName = S.Request, EntityFilters = EntityFilters.Entity })).EntityMetadata;
        var membership = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("rootcomponentbehavior"), TopCount = 1 };
        membership.Criteria.AddCondition("objectid", ConditionOperator.Equal, table.MetadataId!.Value);
        membership.Criteria.AddCondition("componenttype", ConditionOperator.Equal, 1);
        membership.AddLink("solution", "solutionid", "solutionid").LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, Solution);
        var components = service.RetrieveMultiple(membership).Entities;
        if (components.Count == 0 || components[0].GetAttributeValue<OptionSetValue>("rootcomponentbehavior")?.Value != 0)
            service.Execute(new AddSolutionComponentRequest { ComponentId = table.MetadataId.Value, ComponentType = 1,
                SolutionUniqueName = Solution, AddRequiredComponents = false });
        service.Execute(new PublishXmlRequest { ParameterXml = $"<importexportxml><entities><entity>{S.Request}</entity></entities></importexportxml>" });
    }

    private void ApplyMigration007(IOrganizationService service)
    {
        if (!TableExists(service, S.ReadingTable))
            throw new InvalidOperationException("Standard reading table must exist before FM evidence provisioning.");
        FmAttribute(service, S.Request, new MemoAttributeMetadata {
            SchemaName = S.EvidenceReadingIds, DisplayName = new Label("Evidence reading IDs", 1033), MaxLength = 4000 });
        service.Execute(new PublishXmlRequest { ParameterXml = $"<importexportxml><entities><entity>{S.Request}</entity></entities></importexportxml>" });
    }

    private void ApplyMigration008(IOrganizationService service)
    {
        if (!TableExists(service, BuildingTable))
            throw new InvalidOperationException("BMS Building table must exist before FM Request building lookup provisioning.");
        if (!ColumnExists(service, S.Request, S.BuildingLookup))
            service.Execute(new CreateOneToManyRequest
            {
                SolutionUniqueName = Solution,
                Lookup = new LookupAttributeMetadata
                {
                    SchemaName = S.BuildingLookup,
                    DisplayName = new Label("Building", 1033),
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None)
                },
                OneToManyRelationship = new OneToManyRelationshipMetadata
                {
                    SchemaName = "fmc_bmsbuilding_fmrequest",
                    ReferencedEntity = BuildingTable,
                    ReferencingEntity = S.Request,
                    AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                    {
                        Behavior = AssociatedMenuBehavior.UseLabel,
                        Group = AssociatedMenuGroup.Details,
                        Label = new Label("FM Requests", 1033),
                        Order = 10000
                    },
                    CascadeConfiguration = new CascadeConfiguration
                    {
                        Assign = CascadeType.NoCascade,
                        Delete = CascadeType.Restrict,
                        Merge = CascadeType.NoCascade,
                        Reparent = CascadeType.NoCascade,
                        Share = CascadeType.NoCascade,
                        Unshare = CascadeType.NoCascade
                    }
                }
            });
        service.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{S.Request}</entity><entity>{BuildingTable}</entity></entities></importexportxml>"
        });
    }
}
