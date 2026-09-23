using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SPO.Ingestion.Domain;

namespace SPO.Ingestion.Business;

/// <summary>
/// Chuyển từng dòng CSV/JSON/XLSX đã parse thành các bản ghi đích thuần .NET.
/// Mapper chỉ xử lý quy tắc dữ liệu, không kết nối Dataverse và không ghi file.
/// </summary>
public sealed class SpoBronzeMapper(SpoIngestionOptions options)
{
    /// <summary>Tạo GUID ổn định từ business identity để retry vẫn dùng đúng bản ghi cũ.</summary>
    public static Guid StableGuid(string identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    public static string Partition(string objectId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(objectId)));

    /// <summary>Map một nhóm dòng và gom lỗi theo từng dòng thay vì dừng cả file ngay lỗi đầu tiên.</summary>
    public MappingResult Map(SpoSourceDefinition source, IReadOnlyList<ParsedRow> rows, DateTime utcNow)
    {
        var records = new List<BronzeRecord>();
        var issues = new List<RowIssue>();
        var expired = 0;
        foreach (var row in rows)
        {
            try
            {
                // Một dòng reading có thể sinh nhiều point/history vì mỗi metric là một ObjectId.
                foreach (var record in MapRow(source, row, utcNow, () => expired++)) records.Add(record);
            }
            catch (SpoContractException ex)
            {
                issues.Add(new(ex.Ordinal, ex.Code, ex.Message));
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                issues.Add(new(row.Ordinal, "SPO-VALUE", ex.Message));
            }
        }
        return new(records, issues, expired);
    }

    private IEnumerable<BronzeRecord> MapRow(SpoSourceDefinition source, ParsedRow row, DateTime utcNow, Action expired)
    {
        // Mapping version trong config quyết định bộ cột và loại record cần tạo.
        return source.Mapping switch
        {
            "building-v1" => [Building(source, row)],
            "equipment-v1" => [Equipment(source, row, "equipment_id", null,
                ["system_category", "floor_or_zone", "manufacturer", "model", "install_year", "rated_power_kw", "status", "criticality"])],
            "water-meter-v1" => [Equipment(source, row, "water_meter_id", "WaterMeter",
                ["meter_type", "pipe_diameter_mm", "status"])],
            "electric-meter-v1" => [Equipment(source, row, "electric_meter_id", "ElectricMeter",
                ["meter_level", "meter_type", "voltage_level_v", "contract_demand_kw", "utility_region", "status", "data_origin"])],
            "electricity-reading-v1" => Reading(row, "electric_meter_id",
                [("energy_kwh", "Energy", "kWh"), ("demand_kw", "Demand", "kW")],
                true, source, utcNow, expired),
            "water-reading-v1" => Reading(row, "water_meter_id",
                [("consumption_m3", "Consumption", "m3"), ("flow_m3h", "Flow", "m3/h")],
                true, source, utcNow, expired),
            _ => throw new SpoContractException("SPO-MAPPING", row.Ordinal, $"Unknown mapping: {source.Mapping}.")
        };
    }

    private BronzeRecord Building(SpoSourceDefinition source, ParsedRow row)
    {
        // Business key phải thuộc prefix mà source được phép sở hữu.
        var code = Required(row, "building_id", 50);
        RequireOwnedKey(source, row, code);
        var name = Required(row, "building_name", 200);
        var record = new TargetRecord("fmc_bmsbuilding", StableGuid($"metasys-building|{options.SourceId}|{code}"))
        {
            ["fmc_name"] = name,
            ["fmc_buildingcode"] = code,
            ["fmc_sourcebuilding"] = code,
            ["fmc_description"] = Describe(row, "city", "building_type", "ownership", "year_built", "gross_floor_area_m2", "total_floors")
        };
        return new(row.Ordinal, code, BronzeRecordKind.Building, record);
    }

    private BronzeRecord Equipment(SpoSourceDefinition source, ParsedRow row, string codeField,
        string? fixedType, string[] descriptionFields)
    {
        var code = Required(row, codeField, 100);
        RequireOwnedKey(source, row, code);
        var building = Required(row, "building_id", 50);
        var type = fixedType ?? Required(row, "equipment_type", 100);
        // Text loại thiết bị được đổi sang choice number đã cấu hình trong Dataverse.
        if (!options.EquipmentTypeChoices.TryGetValue(type, out var typeValue))
            throw new SpoContractException("SPO-CHOICE", row.Ordinal,
                $"Equipment type '{type}' is not present in fmc_equipmenttype configuration.");
        var record = new TargetRecord("fmc_bmsequipment", StableGuid($"metasys-equipment|{options.SourceId}|{code}"))
        {
            ["fmc_name"] = code,
            ["fmc_equipmentcode"] = code,
            ["fmc_buildingcode"] = building,
            ["fmc_equipmenttype"] = new TargetChoice(typeValue),
            ["fmc_buildingid"] = new TargetReference("fmc_bmsbuilding", StableGuid($"metasys-building|{options.SourceId}|{building}")),
            ["fmc_description"] = Describe(row, descriptionFields)
        };
        return new(row.Ordinal, code, BronzeRecordKind.Equipment, record, ParentIdentity: building);
    }

    private IReadOnlyList<BronzeRecord> Reading(ParsedRow row, string meterField,
        (string Field, string Label, string Unit)[] metrics, bool attachEquipment, SpoSourceDefinition source,
        DateTime utcNow, Action expired)
    {
        var meter = Required(row, meterField, 100);
        RequireOwnedKey(source, row, meter);
        var building = Required(row, "building_id", 100);
        var time = Utc(row, "timestamp", source.TimestampUtcOffset);
        var output = new List<BronzeRecord>();
        foreach (var metric in metrics)
        {
            // Mỗi metric của meter trở thành một point riêng, ví dụ Energy và Demand.
            var value = Decimal(row, metric.Field);
            var objectId = $"{meter}/{metric.Field}";
            var eventIdentity = $"{options.SourceNamespace}|{meter}|{time:O}|{metric.Field}";
            var point = new TargetRecord("fmc_bmspoint", StableGuid($"metasys-point|{options.SourceId}|{objectId}"))
            {
                ["fmc_name"] = $"{meter} {metric.Label}",
                ["fmc_objectid"] = objectId,
                ["fmc_objecttype"] = metric.Label,
                ["fmc_building"] = building,
                ["fmc_buildingcode"] = building,
                ["fmc_currentvalue"] = value,
                ["fmc_unit"] = metric.Unit,
                ["fmc_lastreadingtime"] = time,
                ["fmc_sourcesystem"] = "SharePoint"
            };
            if (attachEquipment)
                point["fmc_equipmentid"] = new TargetReference("fmc_bmsequipment",
                    StableGuid($"metasys-equipment|{options.SourceId}|{meter}"));
            output.Add(new(row.Ordinal, objectId, BronzeRecordKind.Point, point, time,
                ParentIdentity: attachEquipment ? meter : null));

            // History hết TTL vẫn bỏ qua, nhưng current point phía trên vẫn được giữ.
            var remaining = (int)Math.Ceiling((time.AddSeconds(options.HistoryTtlSeconds) - utcNow).TotalSeconds);
            if (remaining <= 0) { expired(); continue; }
            var partition = Partition(objectId);
            var history = new TargetRecord("fmc_bmsreading", StableGuid("spo-reading|" + eventIdentity))
            {
                ["partitionid"] = partition,
                ["fmc_name"] = $"{objectId} {time:O}",
                ["fmc_externalkey"] = eventIdentity.Length <= 200 ? eventIdentity : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(eventIdentity))),
                ["fmc_objectid"] = objectId,
                ["fmc_objectname"] = $"{meter} {metric.Label}",
                ["fmc_objecttype"] = metric.Label,
                ["fmc_building"] = building,
                ["fmc_readingtime"] = time,
                ["fmc_readingvalue"] = value,
                ["fmc_unit"] = metric.Unit,
                ["fmc_sourcesystem"] = "SharePoint",
                ["ttlinseconds"] = remaining
            };
            output.Add(new(row.Ordinal, eventIdentity, BronzeRecordKind.History, history, time, partition));
        }
        return output;
    }

    private static string Required(ParsedRow row, string field, int max)
    {
        // Kiểm tra bắt buộc, khoảng trắng thừa và độ dài tại một chỗ dùng chung.
        if (!row.Values.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            throw new SpoContractException("SPO-REQUIRED", row.Ordinal, $"Required field '{field}' is missing.");
        if (value != value.Trim() || value.Length > max)
            throw new SpoContractException("SPO-TEXT", row.Ordinal, $"Field '{field}' must be trimmed and at most {max} characters.");
        return value;
    }

    private static void RequireOwnedKey(SpoSourceDefinition source, ParsedRow row, string key)
    {
        if (!string.IsNullOrWhiteSpace(source.OwnedKeyPrefix) &&
            !key.StartsWith(source.OwnedKeyPrefix, StringComparison.OrdinalIgnoreCase))
            throw new SpoContractException("SPO-OWNERSHIP", row.Ordinal,
                $"Key '{key}' is outside owned prefix '{source.OwnedKeyPrefix}' for source '{source.Key}'.");
    }

    private static decimal Decimal(ParsedRow row, string field)
    {
        var text = Required(row, field, 100);
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw new SpoContractException("SPO-DECIMAL", row.Ordinal, $"Field '{field}' is not a decimal.");
        // Hợp đồng đích lưu tối đa bốn chữ số thập phân.
        value = decimal.Round(value, 4, MidpointRounding.ToEven);
        if (value is < -100000000000m or > 100000000000m)
            throw new SpoContractException("SPO-RANGE", row.Ordinal, $"Field '{field}' exceeds Dataverse decimal range.");
        return value;
    }

    private static DateTime Utc(ParsedRow row, string field, string configuredOffset)
    {
        // Nếu file không ghi timezone, dùng offset khai báo trong source configuration.
        var text = Required(row, field, 100);
        var sign = configuredOffset.StartsWith('-') ? -1 : 1;
        var offsetText = configuredOffset.TrimStart('+', '-');
        if (!TimeSpan.TryParseExact(offsetText, @"hh\:mm", CultureInfo.InvariantCulture, out var unsignedOffset))
            throw new SpoContractException("SPO-CONFIG", row.Ordinal, $"Timestamp UTC offset '{configuredOffset}' is invalid.");
        var offset = sign < 0 ? -unsignedOffset : unsignedOffset;
        if (offset < TimeSpan.FromHours(-14) || offset > TimeSpan.FromHours(14))
            throw new SpoContractException("SPO-CONFIG", row.Ordinal, $"Timestamp UTC offset '{configuredOffset}' is invalid.");
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed) || parsed.Year < 1753)
            throw new SpoContractException("SPO-DATETIME", row.Ordinal, $"Field '{field}' is not a supported timestamp.");
        var hasExplicitZone = text.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!hasExplicitZone)
        {
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
                throw new SpoContractException("SPO-DATETIME", row.Ordinal, $"Field '{field}' is not a supported timestamp.");
            parsed = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset);
        }
        return parsed.UtcDateTime;
    }

    private static string Describe(ParsedRow row, params string[] fields) => string.Join("; ", fields
        .Where(f => row.Values.TryGetValue(f, out var value) && !string.IsNullOrWhiteSpace(value))
        .Select(f => $"{f}={row.Values[f]}"));
}
