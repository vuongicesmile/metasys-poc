using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DataverseSyncWorker.Contracts;
using DataverseSyncWorker.Models;

namespace DataverseSyncWorker.Services;

/// <summary>
/// Chuyển model đọc từ SQL thành contract thuần .NET để DataAccess ghi sang Dataverse.
/// Mapper chỉ chứa quy tắc dữ liệu; không gọi Dataverse SDK và không thực hiện I/O.
/// </summary>
public sealed class ReadingMapper(SyncOptions options)
{
    // Băm identity thành GUID cố định để cùng một dữ liệu luôn có cùng khóa khi retry.
    public static Guid StableGuid(string identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    // Thêm loại bản ghi và SourceId vào chuỗi băm để các nhóm ID không va chạm nhau.
    public Guid ReadingId(long id) => StableGuid($"metasys-reading|{options.SourceId}|{id}");
    public Guid PointId(string objectId) => StableGuid($"metasys-point|{options.SourceId}|{objectId}");
    public Guid BuildingId(string code) => StableGuid($"metasys-building|{options.SourceId}|{code}");
    public Guid EquipmentId(string code) => StableGuid($"metasys-equipment|{options.SourceId}|{code}");
    public static string Partition(string objectId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(objectId)));

    // Simulator phát UTC; nguồn legacy được đổi từ múi giờ cấu hình sang UTC.
    public DateTime ReadingUtc(BmsReading r) => r.SourceSystem == "Fake Metasys COV"
        ? DateTime.SpecifyKind(r.ReadingTime, DateTimeKind.Utc)
        : ToUtc(r.ReadingTime, options.LegacyReadingTimeZoneId);

    public static DateTime ToUtc(DateTime value, string zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById(zone));

    /// <summary>Kiểm tra giới hạn Dataverse trước khi một dòng được phép đi tiếp.</summary>
    public string? Validate(BmsReading r)
    {
        if (string.IsNullOrWhiteSpace(r.ObjectId) || r.ObjectId.Length > 100) return "Invalid object_id.";
        if (r.ReadingValue is < -100000000000m or > 100000000000m) return "reading_value exceeds Dataverse decimal range.";
        if (r.ReadingTime.Year < 1753) return "reading_time predates supported Dataverse range.";
        return null;
    }

    /// <summary>Tạo trạng thái hiện tại của một point từ reading mới nhất.</summary>
    public DataverseRecord Point(BmsReading r, string? buildingCode = null)
    {
        var e = new DataverseRecord("fmc_bmspoint", PointId(r.ObjectId));
        e["fmc_name"] = r.ObjectName ?? r.ObjectId;
        e["fmc_objectid"] = r.ObjectId;
        e["fmc_objecttype"] = r.ObjectType;
        e["fmc_building"] = r.Building;
        if (!string.IsNullOrWhiteSpace(buildingCode))
            e["fmc_buildingcode"] = buildingCode;
        e["fmc_currentvalue"] = r.ReadingValue;
        e["fmc_unit"] = r.Unit;
        e["fmc_lastreadingtime"] = ReadingUtc(r);
        e["fmc_sourcesystem"] = r.SourceSystem;
        e["fmc_lastsqlid"] = r.Id.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(r.EquipmentCode))
            e["fmc_equipmentid"] = new DataverseReference("fmc_bmsequipment", EquipmentId(r.EquipmentCode));
        return e;
    }

    /// <summary>Chuyển một building trong SQL catalog thành bản ghi đích.</summary>
    public DataverseRecord Building(BmsBuilding row) => new("fmc_bmsbuilding", BuildingId(row.BuildingCode))
    {
        ["fmc_name"] = row.Name,
        ["fmc_buildingcode"] = row.BuildingCode,
        ["fmc_sourcebuilding"] = row.SourceBuilding,
        ["fmc_description"] = row.Description
    };

    /// <summary>Chuyển equipment và lookup building thành bản ghi đích.</summary>
    public DataverseRecord Equipment(BmsEquipment row) => new("fmc_bmsequipment", EquipmentId(row.EquipmentCode))
    {
        ["fmc_name"] = row.Name,
        ["fmc_equipmentcode"] = row.EquipmentCode,
        ["fmc_buildingcode"] = row.BuildingCode,
        ["fmc_equipmenttype"] = new DataverseChoice(BmsRelationManifest.TypeValue(row.EquipmentType)),
        ["fmc_buildingid"] = new DataverseReference("fmc_bmsbuilding", BuildingId(row.BuildingCode)),
        ["fmc_description"] = row.Description
    };

    /// <summary>
    /// Tạo history có TTL tính từ thời gian event; trả null khi dữ liệu đã hết hạn.
    /// </summary>
    public DataverseRecord? History(BmsReading r, DateTime utcNow)
    {
        var time = ReadingUtc(r);
        // TTL dựa trên thời gian event; retry/backfill không được cấp lại đủ 30 ngày cho dữ liệu cũ.
        var remaining = (int)Math.Ceiling((time.AddSeconds(options.HistoryTtlSeconds) - utcNow).TotalSeconds);
        if (remaining <= 0) return null;
        var e = new DataverseRecord("fmc_bmsreading", ReadingId(r.Id));
        e["partitionid"] = Partition(r.ObjectId);
        e["fmc_name"] = $"{r.ObjectId} {time:O}";
        e["fmc_externalkey"] = $"{options.SourceId}-{r.Id}";
        e["fmc_sqlreadingid"] = r.Id.ToString(CultureInfo.InvariantCulture);
        e["fmc_objectid"] = r.ObjectId;
        e["fmc_objectname"] = r.ObjectName;
        e["fmc_objecttype"] = r.ObjectType;
        e["fmc_building"] = r.Building;
        e["fmc_readingtime"] = time;
        e["fmc_readingvalue"] = r.ReadingValue;
        e["fmc_unit"] = r.Unit;
        e["fmc_sourcesystem"] = r.SourceSystem;
        e["fmc_sqlingestedat"] = r.IngestedAt is { } at ? ToUtc(at, options.SqlIngestedTimeZoneId) : null;
        e["ttlinseconds"] = remaining;
        return e;
    }
}
