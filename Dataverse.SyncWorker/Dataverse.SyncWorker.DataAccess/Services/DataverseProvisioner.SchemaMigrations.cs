using Dataverse.SyncWorker.DataAccess.Constants;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.SyncWorker.DataAccess.Services;

public sealed partial class DataverseProvisioner
{
    public void EnsureMigrationLedger()
    {
        // Lấy kết nối đã xác thực và đã kiểm tra đúng OrganizationId.
        var service = connection.Get();

        // Dùng logical name để kiểm tra bảng, không dùng tên hiển thị.
        const string tableLogicalName = DataverseSchema.SchemaMigration.TableLogicalName;

        // Lỗi quyền/kết nối phải nổi lên, không được hiểu thành bảng chưa tồn tại.
        var exists = TableExists(service, tableLogicalName);

        if (!exists)
        {
            CreateMigrationLedger(service);
            Console.WriteLine($"Created table {tableLogicalName}.");
        }
        else Console.WriteLine($"Table {tableLogicalName} already exists.");

        EnsureMigrationVersionColumn(service);
        // đảm bảo cột khi times được áp dụng migration
        EnsureMigrationAppliedOnColumn(service);

        RunMigrations(service);

        service.Execute(new PublishXmlRequest
        {
            ParameterXml = $"<importexportxml><entities><entity>{tableLogicalName}</entity></entities></importexportxml>"
        });
        Console.WriteLine($"Published table {tableLogicalName}.");
    }

    private void EnsureMigrationVersionColumn(
        IOrganizationService service)
    {
        var exists = ColumnExists(
            service,
            DataverseSchema.SchemaMigration.TableLogicalName,
            DataverseSchema.SchemaMigration.VersionLogicalName
        );

        if (exists)
        {
            Console.WriteLine($"Column {DataverseSchema.SchemaMigration.VersionLogicalName} already exists.");
            return;
        }

        // bước tiếp theo mới tạo column
        var versionColumn = new StringAttributeMetadata
        {
            SchemaName =
        DataverseSchema.SchemaMigration.VersionSchemaName,

            DisplayName =
        new Label("Version", 1033),

            RequiredLevel =
        new AttributeRequiredLevelManagedProperty(
            AttributeRequiredLevel.ApplicationRequired
        ),

            MaxLength = 100
        };

        var request = new CreateAttributeRequest
        {
            EntityName =
                DataverseSchema.SchemaMigration.TableLogicalName,

            Attribute = versionColumn,
            SolutionUniqueName = Solution
        };

        service.Execute(request);
        Console.WriteLine($"Created column {DataverseSchema.SchemaMigration.VersionLogicalName}.");
    }


    private void EnsureMigrationAppliedOnColumn(IOrganizationService service)
    {
        var exists = ColumnExists(service,
            DataverseSchema.SchemaMigration.TableLogicalName,
            DataverseSchema.SchemaMigration.AppliedOnLogicalName
        );

        if (exists)
        {
            Console.WriteLine($"Column {DataverseSchema.SchemaMigration.AppliedOnLogicalName} already exists.");
            return;
        }

        // bước kế tiếp: define DateTimeAttributeMetadata
        var appliedOnColumn = new DateTimeAttributeMetadata // định nghĩa 1 column kiểu Datatime
        {
            SchemaName = DataverseSchema.SchemaMigration.AppliedOnLogicalName, // column này tên gì trong dataverse
            DisplayName = new Label("Applied On", 1033), // thấy tên gì
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired), // có bắt buộc nhập ko
            Format = DateTimeFormat.DateAndTime // hiển thị cả ngày giờ hay chỉ ngày
        };

        var request = new CreateAttributeRequest
        {
            EntityName = DataverseSchema.SchemaMigration.TableLogicalName,
            Attribute = appliedOnColumn,
        };

        service.Execute(request);
        Console.WriteLine($"Created column {DataverseSchema.SchemaMigration.AppliedOnLogicalName}.");

    }

    private void CreateMigrationLedger(IOrganizationService service)
    {
        // Chuẩn bị tên, nhãn và ownership của bảng.
        var tableMetadata = BuildMigrationLedgerMetaData();

        // Chuẩn bị cột primary name bắt buộc của Dataverse.
        var primaryNameMetadata = BuildMigrationLedgerPrimaryName();

        // Ghép metadata thành request tạo bảng thuộc solution FMCentralBms.
        var request = new CreateEntityRequest
        {
            // Gắn bảng vào solution để có thể export/deploy về sau.
            SolutionUniqueName = Solution,
            // Định nghĩa bảng cần tạo.
            Entity = tableMetadata,
            // Định nghĩa cột primary name.
            PrimaryAttribute = primaryNameMetadata
        };

        // Gửi request; nếu Dataverse từ chối thì để caller thấy lỗi thật.
        service.Execute(request);
    }

    private bool MigrationAlreadyApplied(IOrganizationService service, string version)
    {
        // bước sau sẽ query fmc_schemamigration
        var query = new QueryExpression(
        DataverseSchema.SchemaMigration.TableLogicalName)
        {
            ColumnSet = new ColumnSet(false), // tôi ko càn lấy dữ liệu của các column về, chỉ cần biết record có tồn tại hay ko
            TopCount = 1, // tìm thấy 1 record là đủ
        };


        query.Criteria.AddCondition(
            DataverseSchema.SchemaMigration.VersionLogicalName,
            ConditionOperator.Equal,
            version
        ); // giống như query sql: Where fmc_version = @version

        var result = service.RetrieveMultiple(query);

        return result.Entities.Count > 0;
    }

    private void ApplyMigration001( IOrganizationService service )
    {
        // logic thay đổi schema thật sự sẽ nằm ở đây
        Console.WriteLine("Applying migration 001...");

    }

    private void RunMigrations(IOrganizationService service)
    {
        const string version = "001";

        if (MigrationAlreadyApplied(service, version))
        {
            return;
        }

        ApplyMigration001(service);

        RecordMigration(
            service,
            version,
            "Initial migration"
        );
    }

    private void RecordMigration(
    IOrganizationService service,
    string version,
    string name)
    {
        var migration = new Entity(
            DataverseSchema.SchemaMigration.TableLogicalName
        );

        migration[
            DataverseSchema.SchemaMigration.PrimaryNameLogicalName
        ] = name;

        migration[
            DataverseSchema.SchemaMigration.VersionLogicalName
        ] = version;

        migration[
            DataverseSchema.SchemaMigration.AppliedOnLogicalName
        ] = DateTime.UtcNow;

        service.Create(migration);
    }
}
