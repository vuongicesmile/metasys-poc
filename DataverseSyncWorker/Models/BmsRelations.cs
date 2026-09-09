using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DataverseSyncWorker.Models;

public sealed record BuildingSeed(string Code, string Name, string SourceBuilding, string? Description);
public sealed record EquipmentSeed(string Code, string Name, string Type, string BuildingCode, string? Description);
public sealed record PointMappingSeed(string ObjectId, string EquipmentCode);

public sealed record BmsRelationManifest(
    string Version, string SourceId, BuildingSeed[] Buildings,
    EquipmentSeed[] Equipment, PointMappingSeed[] PointMappings)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static BmsRelationManifest Load(string path, string sourceId)
    {
        var manifest = JsonSerializer.Deserialize<BmsRelationManifest>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException("Empty relationship manifest.");
        manifest.Validate(sourceId);
        return manifest;
    }

    public static int TypeValue(string type) => type switch
    {
        "WaterMeter" => 789100000,
        "TemperatureSensor" => 789100001,
        "TestRig" => 789100002,
        _ => throw new InvalidOperationException($"Unknown equipment type: {type}.")
    };

    public void Validate(string sourceId)
    {
        CheckText(Version, 100, "version");
        if (SourceId != sourceId) throw new InvalidOperationException("Manifest SourceId differs from worker SourceId.");
        if (Buildings is null || Equipment is null || PointMappings is null ||
            Buildings.Length == 0 || Equipment.Length == 0 || PointMappings.Length == 0)
            throw new InvalidOperationException("Manifest must contain buildings, equipment and pointMappings.");
        if (Buildings.Any(x => x is null) || Equipment.Any(x => x is null) || PointMappings.Any(x => x is null))
            throw new InvalidOperationException("Manifest arrays cannot contain null items.");
        Unique(Buildings.Select(x => x.Code), "building code");
        Unique(Buildings.Select(x => x.SourceBuilding), "source building");
        Unique(Equipment.Select(x => x.Code), "equipment code");
        Unique(PointMappings.Select(x => x.ObjectId), "point Object ID");
        foreach (var b in Buildings)
        {
            CheckCode(b.Code, 50); CheckText(b.Name, 200, "building name");
            CheckText(b.SourceBuilding, 100, "source building"); CheckDescription(b.Description);
        }
        foreach (var e in Equipment)
        {
            CheckCode(e.Code, 100); CheckText(e.Name, 200, "equipment name");
            _ = TypeValue(e.Type); CheckDescription(e.Description);
            if (!Buildings.Any(b => b.Code == e.BuildingCode))
                throw new InvalidOperationException($"Equipment {e.Code}: missing building {e.BuildingCode}.");
        }
        foreach (var p in PointMappings)
        {
            CheckText(p.ObjectId, 100, "Object ID");
            if (!Equipment.Any(e => e.Code == p.EquipmentCode))
                throw new InvalidOperationException($"Point {p.ObjectId}: missing equipment {p.EquipmentCode}.");
        }
    }

    private static void CheckCode(string value, int length)
    {
        CheckText(value, length, "code");
        if (!Regex.IsMatch(value, "^[A-Z0-9][A-Z0-9_-]*$"))
            throw new InvalidOperationException($"Code must use uppercase ASCII letters, digits, hyphen or underscore: {value}.");
    }
    private static void CheckText(string? value, int length, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > length || value != value.Trim())
            throw new InvalidOperationException($"Invalid {field}: required, no surrounding whitespace, maximum {length} characters.");
    }
    private static void CheckDescription(string? value)
    {
        if (value?.Length > 2000) throw new InvalidOperationException("Description exceeds 2000 characters.");
    }
    private static void Unique(IEnumerable<string> values, string field)
    {
        if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count())
            throw new InvalidOperationException($"Duplicate {field} in manifest.");
    }
}

public sealed record BmsRelationCommand(string Mode, bool Apply, string ManifestPath, string? ReceiptPath)
{
    public static readonly string[] Modes =
        ["--bms-relations-status", "--provision-bms-relations", "--seed-bms-relations", "--verify-bms-relations", "--self-test-bms-relations"];

    public static (BmsRelationCommand? Command, string[] Remaining) Parse(string[] args)
    {
        var modes = args.Where(Modes.Contains).ToArray();
        var special = args.Any(a => a is "--apply" or "--dry-run" or "--manifest" or "--receipt");
        if (modes.Length == 0)
        {
            if (special || args.Any(a => a.Contains("bms-relations", StringComparison.Ordinal)))
                throw new InvalidOperationException("A valid BMS relationship command is required; refusing to start the worker.");
            return (null, args);
        }
        if (modes.Length != 1 || args.Any(a => a is "--provision" or "--run-once" or "--verify" or
                "--self-test" or "--enqueue" or "--process-command-once"))
            throw new InvalidOperationException("Choose exactly one maintenance command.");
        var remaining = new List<string>();
        var apply = false; var dryRun = false;
        var manifest = Path.Combine(AppContext.BaseDirectory, "config", "bms-relations.seed.json");
        string? receipt = null;
        var seen = new HashSet<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (Modes.Contains(arg)) continue;
            if (arg is "--apply" or "--dry-run" or "--manifest" or "--receipt")
            {
                if (!seen.Add(arg)) throw new InvalidOperationException($"Duplicate argument: {arg}.");
                if (arg == "--apply") apply = true;
                else if (arg == "--dry-run") dryRun = true;
                else
                {
                    if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new InvalidOperationException($"{arg} requires a file path.");
                    if (arg == "--manifest") manifest = Path.GetFullPath(args[i]);
                    else receipt = Path.GetFullPath(args[i]);
                }
            }
            else
            {
                // Only explicitly named configuration overrides belong to the host parser.
                if (!arg.StartsWith("--Dataverse:", StringComparison.Ordinal) || !arg.Contains('='))
                    throw new InvalidOperationException($"Unknown relationship argument: {arg}.");
                remaining.Add(arg);
            }
        }
        if (apply && dryRun) throw new InvalidOperationException("Choose --apply or --dry-run, not both.");
        if ((apply || dryRun || receipt is not null) && modes[0] != "--seed-bms-relations")
            throw new InvalidOperationException("--apply, --dry-run and --receipt only apply to --seed-bms-relations.");
        return (new(modes[0], apply, manifest, receipt), remaining.ToArray());
    }
}
