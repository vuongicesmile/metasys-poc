namespace Dataverse.SyncWorker.DataAccess.Constants;

// Gom tên schema để các bước tạo và truy vấn bảng không dùng nhầm tên.
public static class DataverseSchema
{
    public static class SchemaMigration
    {
        // Logical name là tên chữ thường Dataverse dùng để truy vấn bảng.
        public const string TableLogicalName = "fmc_schemamigration";

        // Schema name là tên kỹ thuật dùng trong request tạo bảng.
        public const string TableSchemaName = "fmc_SchemaMigration";

        // Cột primary name bắt buộc của mỗi bảng Dataverse.
        public const string PrimaryNameSchemaName = "fmc_Name";
        public const string PrimaryNameLogicalName = "fmc_name";

        // Các tên dưới đây dành cho cột tracking ở bước migration tiếp theo.
        // Version định danh phiên bản migration đã áp dụng.
        public const string VersionSchemaName = "fmc_Version";
        public const string VersionLogicalName = "fmc_version";

        // AppliedOn lưu thời điểm áp dụng migration.
        public const string AppliedOnSchemaName = "fmc_AppliedOn";
        public const string AppliedOnLogicalName = "fmc_appliedon";

        // Checksum dùng để nhận biết nội dung migration bị thay đổi.
        public const string ChecksumSchemaName = "fmc_Checksum";
    }


    public static class FmRequest
    {
        public const string TableLogicalName = "fmc_fmrequest";
        public const string TableSchemaName = "fmc_FmRequest";

        public const string PrimaryNameLogicalName = "fmc_name";
        public const string PrimaryNameSchemaName = "fmc_Name";

        public const string DescriptionLogicalName = "fmc_description";
        public const string DescriptionSchemaName = "fmc_Description";

        public const string StatusLogicalName = "fmc_status";
        public const string StatusSchemaName = "fmc_Status";
    }

    public static class FmRequestStatus
    {
        public const int Draft = 100000000;
        public const int Submitted = 100000001;
        public const int Approved = 100000002;
        public const int Rejected = 100000003;
    }

}
