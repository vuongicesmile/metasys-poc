using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;

namespace DataverseSyncWorker.Services;

// Partial class này chỉ dựng metadata; request gửi Dataverse nằm trong SchemaMigrations.cs.
public sealed partial class DataverseProvisioner
{
    private static EntityMetadata BuildMigrationLedgerMetaData()
    {
        // EntityMetadata mô tả bảng, chưa thực hiện ghi lên Dataverse.
        return new EntityMetadata
        {
            // Tên kỹ thuật; Dataverse suy ra logical name fmc_schemamigration.
            SchemaName = DataverseSchema.SchemaMigration.TableSchemaName,
            // 1033 là mã ngôn ngữ English cho nhãn trong maker portal.
            DisplayName = new Label("Schema migration", 1033),
            // Tên hiển thị khi xem danh sách nhiều bản ghi.
            DisplayCollectionName = new Label("Schema migrations", 1033),
            // Mô tả công dụng của bảng cho người quản trị.
            Description = new Label("Track applied Dataverse schema migrations", 1033),
            // Bảng thuộc organization, không có owner riêng cho từng bản ghi.
            OwnershipType = OwnershipTypes.OrganizationOwned,
            // Bảng dữ liệu thông thường, không phải activity như Task hoặc Email.
            IsActivity = false
        };
    }

    private static StringAttributeMetadata BuildMigrationLedgerPrimaryName()
    {
        // Mỗi bảng Dataverse cần một cột primary name dạng text.
        return new StringAttributeMetadata
        {
            // Tên kỹ thuật của cột; logical name tương ứng là fmc_name.
            SchemaName = DataverseSchema.SchemaMigration.PrimaryNameSchemaName,
            // Nhãn hiển thị của cột primary name.
            DisplayName = new Label("Name", 1033),
            // Bắt buộc có tên khi tạo bản ghi migration.
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
            // Giới hạn tên ở 200 ký tự.
            MaxLength = 200
        };
    }
}
