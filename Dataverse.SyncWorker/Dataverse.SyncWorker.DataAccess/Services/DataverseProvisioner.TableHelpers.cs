using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace Dataverse.SyncWorker.DataAccess.Services;

// Các thao tác metadata dùng chung cho các migration.
public sealed partial class DataverseProvisioner
{
    private static bool TableExists(IOrganizationService service, string logicalName)
    {
        try
        {
            // Chỉ hỏi metadata cấp bảng, không đọc bản ghi dữ liệu.
            var request = new RetrieveEntityRequest
            {
                // Dataverse tìm bảng bằng logical name.
                LogicalName = logicalName,
                // Không cần lấy toàn bộ danh sách cột hoặc quan hệ.
                EntityFilters = EntityFilters.Entity,
                // Thấy cả metadata vừa tạo nhưng chưa publish.
                RetrieveAsIfPublished = true
            };

            // Request thành công nghĩa là bảng tồn tại.
            service.Execute(request);
            return true;
        }
        // Chỉ coi lỗi “không tìm thấy đúng bảng này” là bảng chưa tồn tại.
        catch (FaultException<OrganizationServiceFault> ex) when
            (ex.Message.Contains(logicalName, StringComparison.OrdinalIgnoreCase) &&
             (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
              ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            // Lỗi quyền, kết nối hoặc lỗi Dataverse khác sẽ tiếp tục được ném ra.
            return false;
        }
    }



    private bool ColumnExists(
    IOrganizationService service,
    string tableLogicalName,
    string columnLogicalName)
    {
        try
        {
            var request = new RetrieveAttributeRequest
            {
                EntityLogicalName = tableLogicalName,
                LogicalName = columnLogicalName,
                RetrieveAsIfPublished = true
            };

            service.Execute(request);

            return true;
        }
        catch (FaultException<OrganizationServiceFault> ex) when
            (ex.Message.Contains(tableLogicalName, StringComparison.OrdinalIgnoreCase) &&
             ex.Message.Contains(columnLogicalName, StringComparison.OrdinalIgnoreCase) &&
             (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
              ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
              ex.Message.Contains("Could not find an attribute", StringComparison.OrdinalIgnoreCase)))
        {
            // Dataverse dùng "Could not find an attribute" cho cột chưa tồn tại.
            return false;
        }
    }
}
