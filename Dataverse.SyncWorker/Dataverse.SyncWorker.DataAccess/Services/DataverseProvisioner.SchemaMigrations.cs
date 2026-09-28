using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace DataverseSyncWorker.Services;

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

        // Bảng đã tồn tại thì dừng để command có thể chạy lại an toàn.
        if (exists) return;

        // Chỉ gửi request tạo bảng khi metadata chưa có.
        CreateMigrationLedger(service);
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
}
