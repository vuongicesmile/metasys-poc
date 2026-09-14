using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpoIngestion.Core;

public static class SpoConfiguration
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static SpoIngestionOptions Load(string path)
    {
        var options = JsonSerializer.Deserialize<SpoIngestionOptions>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException("Empty SPO ingestion configuration.");
        Validate(options);
        return options;
    }

    public static SpoSourceDefinition Resolve(SpoIngestionOptions options, string sourcePath)
    {
        var normalized = NormalizePath(sourcePath);
        var extension = Path.GetExtension(normalized).TrimStart('.');
        var matches = options.Sources.Where(source =>
            normalized.StartsWith(NormalizePath(source.PathPrefix), StringComparison.OrdinalIgnoreCase) &&
            source.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Expected one mapping for '{sourcePath}', found {matches.Length}.");
        if (!matches[0].Enabled)
            throw new InvalidOperationException($"Source '{matches[0].Key}' is disabled because target contract is not available.");
        return matches[0];
    }

    private static void Validate(SpoIngestionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceNamespace) || string.IsNullOrWhiteSpace(options.SourceId))
            throw new InvalidOperationException("SourceNamespace and SourceId are required.");
        if (options.BatchSize is < 1 or > 100 || options.HistoryTtlSeconds <= 0 || options.MaxFileBytes <= 0)
            throw new InvalidOperationException("BatchSize must be 1..100; HistoryTtlSeconds and MaxFileBytes must be positive.");
        if (options.Sources.Length == 0 || options.Sources.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Sources.Length)
            throw new InvalidOperationException("Sources are required and source keys must be unique.");
        foreach (var source in options.Sources)
            if (string.IsNullOrWhiteSpace(source.PathPrefix) || source.Extensions.Length == 0 || source.MaxRows <= 0)
                throw new InvalidOperationException($"Invalid source definition: {source.Key}.");
            else if (source.Enabled && string.IsNullOrWhiteSpace(source.OwnedKeyPrefix))
                throw new InvalidOperationException($"Enabled source must declare OwnedKeyPrefix: {source.Key}.");
    }

    private static string NormalizePath(string path) => "/" + path.Replace('\\', '/').Trim('/');
}
