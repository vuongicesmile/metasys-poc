using System.Text.Json;
using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;
using SPO.Ingestion.Business;
using SPO.Ingestion.Common;
using SPO.Ingestion.DataAccess;
using SPO.Ingestion.Domain;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  SPO.Ingestion.Cli preview-all [--root <folder>] [--config <file>] [--utc-now <ISO timestamp>]");
    Console.WriteLine("  SPO.Ingestion.Cli ingest-local [--root <folder>] [--config <file>] [--appsettings <file>] [--source <source-key>] [--utc-now <ISO timestamp>] [--current-only]");
    Console.WriteLine("  SPO.Ingestion.Cli ingest-dataverse-once [--config <file>] [--appsettings <file>] [--max <1..100>] [--utc-now <ISO timestamp>]");
    Console.WriteLine("  SPO.Ingestion.Cli watch-dataverse [--config <file>] [--appsettings <file>] [--max <1..100>] [--poll-seconds <seconds>]");
    return;
}
string Arg(string name, string fallback)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}
var root = Path.GetFullPath(Arg("--root", Path.Combine(Environment.CurrentDirectory, "data")));
var configPath = Path.GetFullPath(Arg("--config", Path.Combine(Environment.CurrentDirectory, "config", "spo-ingestion.json")));
var utcNow = DateTime.Parse(Arg("--utc-now", DateTime.UtcNow.ToString("O")), null,
    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
var options = SpoConfiguration.Load(configPath);

if (args[0] is "ingest-dataverse-once" or "watch-dataverse")
{
    var appsettingsPath = Path.GetFullPath(Arg("--appsettings", Path.Combine(Environment.CurrentDirectory, "DataverseSyncWorker", "appsettings.json")));
    var maxFiles = int.Parse(Arg("--max", "20"), System.Globalization.CultureInfo.InvariantCulture);
    var pollSeconds = int.Parse(Arg("--poll-seconds", "10"), System.Globalization.CultureInfo.InvariantCulture);
    if (pollSeconds is < 2 or > 3600) throw new ArgumentOutOfRangeException("--poll-seconds", "poll-seconds must be 2..3600.");
    var dataverse = LoadDataverseOptions(appsettingsPath);
    using var connection = new DataverseConnection(dataverse);
    var client = connection.Get();
    var writer = new DataverseBronzeWriter(client, options);
    var mapper = new SpoBronzeMapper(options);
    var local = new SpoLocalProcessor(new TabularParser(), mapper, new SpoRecordValidator(mapper), writer, options);
    var inbox = new SpoDataverseInboxProcessor(client, local, options);
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };

    do
    {
        var cycleUtc = args[0] == "watch-dataverse" ? DateTime.UtcNow : utcNow;
        var cycle = await inbox.ProcessOnce(maxFiles, cycleUtc, stop.Token);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            utcNow = cycleUtc,
            count = cycle.Count,
            imported = cycle.Count(x => x.Status == "Imported"),
            waiting = cycle.Count(x => x.Status == "WaitingDependency"),
            failed = cycle.Count(x => x.Status == "Failed"),
            files = cycle
        }, SpoConfiguration.Json));
        if (args[0] == "ingest-dataverse-once")
        {
            if (cycle.Any(x => x.Status == "Failed")) Environment.ExitCode = 1;
            return;
        }
        try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    } while (!stop.IsCancellationRequested);
    return;
}

if (args[0] == "ingest-local")
{
    var appsettingsPath = Path.GetFullPath(Arg("--appsettings", Path.Combine(Environment.CurrentDirectory, "DataverseSyncWorker", "appsettings.json")));
    var sourceKey = Arg("--source", "");
    var currentOnly = args.Contains("--current-only", StringComparer.Ordinal);
    var dataverse = LoadDataverseOptions(appsettingsPath);
    using var connection = new DataverseConnection(dataverse);
    var writer = new DataverseBronzeWriter(connection.Get(), options);
    var mapper = new SpoBronzeMapper(options);
    var runner = new SpoLocalProcessor(new TabularParser(), mapper, new SpoRecordValidator(mapper), writer, options);
    var selected = options.Sources
        .Where(x => x.Enabled && x.LocalSample is not null)
        .Where(x => string.IsNullOrWhiteSpace(sourceKey) || x.Key.Equals(sourceKey, StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.Mapping.EndsWith("building-v1", StringComparison.Ordinal) ? 0 :
            x.Mapping is "equipment-v1" or "water-meter-v1" or "electric-meter-v1" ? 1 : 2)
        .ToArray();
    if (selected.Length == 0) throw new InvalidOperationException($"No enabled local sample matched source '{sourceKey}'.");

    var ingestResults = new List<LocalIngestionResult>();
    foreach (var source in selected)
    {
        var file = Path.Combine(root, source.LocalSample!);
        if (!File.Exists(file)) throw new FileNotFoundException($"Local sample not found for {source.Key}.", file);
        await using var content = File.OpenRead(file);
        var sourcePath = source.PathPrefix.TrimEnd('/') + "/" + Path.GetFileName(file);
        var result = await runner.Process(content, sourcePath, utcNow, CancellationToken.None, currentOnly);
        ingestResults.Add(result);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            result.Status,
            result.SourceKey,
            result.Mapping,
            result.SourcePath,
            result.InputRows,
            result.Delivered,
            result.Skipped,
            result.TargetCounts,
            issueCount = result.Issues.Count,
            receiptCount = result.Receipts.Count,
            issueSample = result.Issues.Take(10)
        }, SpoConfiguration.Json));
        if (result.Status != "Completed") Environment.ExitCode = 1;
    }

    var receiptDirectory = Path.Combine(Environment.CurrentDirectory, ".artifacts", "spo-local");
    Directory.CreateDirectory(receiptDirectory);
    var receiptPath = Path.Combine(receiptDirectory, $"receipt-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.json");
    await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(new { utcNow, results = ingestResults }, SpoConfiguration.Json));
    Console.WriteLine($"Receipt: {receiptPath}");
    return;
}
if (args[0] != "preview-all") throw new InvalidOperationException("Unknown command. Use --help.");
var previewer = new SpoPreviewer(new TabularParser(), new SpoBronzeMapper(options));
var results = new List<object>();
var exit = 0;
foreach (var source in options.Sources.Where(x => x.LocalSample is not null))
{
    var file = Path.Combine(root, source.LocalSample!);
    if (!File.Exists(file)) { results.Add(new { source.Key, status = "MissingLocalSample", file }); exit = 1; continue; }
    if (!source.Enabled) { results.Add(new { source.Key, status = "DisabledContract", file, source.Mapping }); continue; }
    try
    {
        await using var content = File.OpenRead(file);
        var sourcePath = source.PathPrefix.TrimEnd('/') + "/" + Path.GetFileName(file);
        var preview = previewer.Preview(content, sourcePath, options, utcNow);
        results.Add(new
        {
            status = preview.Issues.Count == 0 ? "Ready" : "Invalid",
            file,
            preview.SourceKey,
            preview.Mapping,
            preview.InputRows,
            preview.TargetCounts,
            issueCount = preview.Issues.Count,
            issueSample = preview.Issues.Take(20)
        });
        if (preview.Issues.Count > 0) exit = 1;
    }
    catch (Exception ex) { results.Add(new { source.Key, status = "Error", file, error = ex.Message }); exit = 1; }
}
Console.WriteLine(JsonSerializer.Serialize(new { utcNow, results }, SpoConfiguration.Json));
Environment.ExitCode = exit;

static SyncOptions LoadDataverseOptions(string path)
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    if (!document.RootElement.TryGetProperty("Dataverse", out var section))
        throw new InvalidOperationException($"Dataverse section is missing in '{path}'.");
    var options = JsonSerializer.Deserialize<SyncOptions>(section.GetRawText(), SpoConfiguration.Json)
        ?? throw new InvalidOperationException($"Dataverse section is invalid in '{path}'.");
    options.Validate();
    return options;
}
