using Dataverse.SyncWorker.DataAccess.Constants;
using Microsoft.Crm.Sdk.Messages;

namespace Dataverse.SyncWorker.DataAccess.Services;

public sealed partial class DataverseProvisioner
{
    public void EnsureMigrationLedger()
    {
        // connection.Get() xác thực và kiểm tra đúng OrganizationId trước khi ghi metadata.
        var service = connection.Get();
        const string table = DataverseSchema.SchemaMigration.TableLogicalName;

        if (TableExists(service, table))
            Console.WriteLine($"Table {table} already exists.");
        else
        {
            CreateMigrationLedger(service);
            Console.WriteLine($"Created table {table}.");
        }

        EnsureMigrationVersionColumn(service);
        EnsureMigrationAppliedOnColumn(service);
        RunMigrations(service);

        service.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{table}</entity></entities></importexportxml>"
        });
        Console.WriteLine($"Published table {table}.");
    }
}
