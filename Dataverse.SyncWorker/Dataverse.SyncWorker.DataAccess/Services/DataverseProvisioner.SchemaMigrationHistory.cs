using Dataverse.SyncWorker.DataAccess.Constants;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.SyncWorker.DataAccess.Services;

public sealed partial class DataverseProvisioner
{
    private void RunMigrations(IOrganizationService service)
    {
        const string version = "001";
        if (MigrationAlreadyApplied(service, version)) return;

        ApplyMigration001(service);
        RecordMigration(service, version, "Initial migration");
    }

    private static bool MigrationAlreadyApplied(IOrganizationService service, string version)
    {
        var query = new QueryExpression(DataverseSchema.SchemaMigration.TableLogicalName)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1
        };
        query.Criteria.AddCondition(DataverseSchema.SchemaMigration.VersionLogicalName,
            ConditionOperator.Equal, version);
        return service.RetrieveMultiple(query).Entities.Count > 0;
    }

    private static void ApplyMigration001(IOrganizationService service)
    {
        // Chưa có thay đổi schema cho version 001; giữ nguyên hành vi hiện tại.
        Console.WriteLine("Applying migration 001...");
    }

    private static void RecordMigration(IOrganizationService service, string version, string name)
    {
        var migration = new Entity(DataverseSchema.SchemaMigration.TableLogicalName)
        {
            [DataverseSchema.SchemaMigration.PrimaryNameLogicalName] = name,
            [DataverseSchema.SchemaMigration.VersionLogicalName] = version,
            [DataverseSchema.SchemaMigration.AppliedOnLogicalName] = DateTime.UtcNow
        };
        service.Create(migration);
    }
}
