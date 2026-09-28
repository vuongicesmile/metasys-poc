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

        // Các tên dưới đây dành cho cột tracking ở bước migration tiếp theo.
        // Version định danh phiên bản migration đã áp dụng.
        public const string VersionSchemaName = "fmc_Version";
        public const string VersionLogicalName = "fmc_version";

        // AppliedOn lưu thời điểm áp dụng migration.
        public const string AppliedOnSchemaName = "fmc_AppliedOn";

        // Checksum dùng để nhận biết nội dung migration bị thay đổi.
        public const string ChecksumSchemaName = "fmc_Checksum";
    }
}
