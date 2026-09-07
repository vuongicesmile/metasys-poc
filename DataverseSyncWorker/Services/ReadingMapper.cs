using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DataverseSyncWorker.Models;
using Microsoft.Xrm.Sdk;

namespace DataverseSyncWorker.Services;

public sealed class ReadingMapper(SyncOptions options)
{
    // Stable primary GUID + partition, not an unsupported elastic custom alternate key.
    public static Guid StableGuid(string identity)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    public Guid ReadingId(long id) => StableGuid($"metasys-reading|{options.SourceId}|{id}");
    public Guid PointId(string objectId) => StableGuid($"metasys-point|{options.SourceId}|{objectId}");
    public static string Partition(string objectId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(objectId)));

    public DateTime ReadingUtc(BmsReading r) => r.SourceSystem == "Fake Metasys COV"
        ? DateTime.SpecifyKind(r.ReadingTime, DateTimeKind.Utc)
        : ToUtc(r.ReadingTime, options.LegacyReadingTimeZoneId);

    public static DateTime ToUtc(DateTime value, string zone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById(zone));

    public string? Validate(BmsReading r)
    {
        if (string.IsNullOrWhiteSpace(r.ObjectId) || r.ObjectId.Length > 100) return "Invalid object_id.";
        if (r.ReadingValue is < -100000000000m or > 100000000000m) return "reading_value exceeds Dataverse decimal range.";
        if (r.ReadingTime.Year < 1753) return "reading_time predates supported Dataverse range.";
        return null;
    }

    public Entity Point(BmsReading r)
    {
        var e = new Entity("fmc_bmspoint", PointId(r.ObjectId));
        e["fmc_name"] = r.ObjectName ?? r.ObjectId;
        e["fmc_objectid"] = r.ObjectId;
        e["fmc_objecttype"] = r.ObjectType;
        e["fmc_building"] = r.Building;
        e["fmc_currentvalue"] = r.ReadingValue;
        e["fmc_unit"] = r.Unit;
        e["fmc_lastreadingtime"] = ReadingUtc(r);
        e["fmc_sourcesystem"] = r.SourceSystem;
        e["fmc_lastsqlid"] = r.Id.ToString(CultureInfo.InvariantCulture);
        return e;
    }

    public Entity? History(BmsReading r, DateTime utcNow)
    {
        var time = ReadingUtc(r);
        // Age-based retention: retrying/backfilling must not grant an old row another 30 days.
        var remaining = (int)Math.Ceiling((time.AddSeconds(options.HistoryTtlSeconds) - utcNow).TotalSeconds);
        if (remaining <= 0) return null;
        var e = new Entity("fmc_bmsreading", ReadingId(r.Id));
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
