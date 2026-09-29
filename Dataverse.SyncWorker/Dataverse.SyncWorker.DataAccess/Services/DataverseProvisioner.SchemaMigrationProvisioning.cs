using Dataverse.SyncWorker.DataAccess.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Dataverse.SyncWorker.DataAccess.Services;

public sealed partial class DataverseProvisioner
{
    private void CreateMigrationLedger(IOrganizationService service)
    {
        service.Execute(new CreateEntityRequest
        {
            SolutionUniqueName = Solution,
            Entity = BuildMigrationLedgerMetaData(),
            PrimaryAttribute = BuildMigrationLedgerPrimaryName()
        });
    }

    private void EnsureMigrationVersionColumn(IOrganizationService service)
    {
        const string table = DataverseSchema.SchemaMigration.TableLogicalName;
        const string column = DataverseSchema.SchemaMigration.VersionLogicalName;
        if (ColumnExists(service, table, column))
        {
            Console.WriteLine($"Column {column} already exists.");
            return;
        }

        service.Execute(new CreateAttributeRequest
        {
            EntityName = table,
            SolutionUniqueName = Solution,
            Attribute = new StringAttributeMetadata
            {
                SchemaName = DataverseSchema.SchemaMigration.VersionSchemaName,
                DisplayName = new Label("Version", 1033),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                MaxLength = 100
            }
        });
        Console.WriteLine($"Created column {column}.");
    }

    private void EnsureMigrationAppliedOnColumn(IOrganizationService service)
    {
        const string table = DataverseSchema.SchemaMigration.TableLogicalName;
        const string column = DataverseSchema.SchemaMigration.AppliedOnLogicalName;
        if (ColumnExists(service, table, column))
        {
            Console.WriteLine($"Column {column} already exists.");
            return;
        }

        service.Execute(new CreateAttributeRequest
        {
            EntityName = table,
            SolutionUniqueName = Solution,
            Attribute = new DateTimeAttributeMetadata
            {
                SchemaName = DataverseSchema.SchemaMigration.AppliedOnSchemaName,
                DisplayName = new Label("Applied On", 1033),
                RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                Format = DateTimeFormat.DateAndTime
            }
        });
        Console.WriteLine($"Created column {column}.");
    }
}
