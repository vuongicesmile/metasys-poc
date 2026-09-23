using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SPO.Ingestion.App.Hosting;
using SPO.Ingestion.Business.Abstractions;
using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;
using SPO.Ingestion.Business;
using SPO.Ingestion.Common;
using SPO.Ingestion.DataAccess;
using SPO.Ingestion.Domain;

// Không có command hoặc có --help thì chỉ in hướng dẫn, không mở kết nối Dataverse.
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
    // Tìm giá trị ngay sau tên option; dùng fallback khi option không xuất hiện.
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}
var root = Path.GetFullPath(Arg("--root", Path.Combine(Environment.CurrentDirectory, "data")));
var configPath = Path.GetFullPath(Arg("--config", Path.Combine(Environment.CurrentDirectory, "config", "spo-ingestion.json")));
var utcNow = DateTime.Parse(Arg("--utc-now", DateTime.UtcNow.ToString("O")), null,
    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
var options = SpoConfiguration.Load(configPath);

// Hai command này đọc các file đã archive trong Dataverse inbox.
// ingest-dataverse-once chạy một vòng; watch-dataverse tiếp tục polling đến khi Ctrl+C.
if (args[0] is "ingest-dataverse-once" or "watch-dataverse")
{
    var appsettingsPath = Path.GetFullPath(Arg("--appsettings", Path.Combine(Environment.CurrentDirectory, "Dataverse.SyncWorker", "Dataverse.SyncWorker.App", "appsettings.json")));
    var maxFiles = int.Parse(Arg("--max", "20"), System.Globalization.CultureInfo.InvariantCulture);
    var pollSeconds = int.Parse(Arg("--poll-seconds", "10"), System.Globalization.CultureInfo.InvariantCulture);
    if (pollSeconds is < 2 or > 3600) throw new ArgumentOutOfRangeException("--poll-seconds", "poll-seconds must be 2..3600.");
    var dataverse = LoadDataverseOptions(appsettingsPath);
    // CLI là composition root: tại đây mới tạo implementation DataAccess thật.
    await using var services = BuildServices(options, dataverse);
    var inbox = services.GetRequiredService<SpoDataverseInboxProcessor>();
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };

    do
    {
        // Watch mode dùng thời gian thật mỗi vòng; one-shot giữ utcNow do caller truyền vào.
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
        // Delay có cancellation để Ctrl+C dừng nhanh mà không chờ hết poll interval.
        try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stop.Token); }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    } while (!stop.IsCancellationRequested);
    return;
}

// ingest-local đọc sample trên máy nhưng vẫn dùng cùng mapper/validator/writer với cloud path.
if (args[0] == "ingest-local")
{
    var appsettingsPath = Path.GetFullPath(Arg("--appsettings", Path.Combine(Environment.CurrentDirectory, "Dataverse.SyncWorker", "Dataverse.SyncWorker.App", "appsettings.json")));
    var sourceKey = Arg("--source", "");
    var currentOnly = args.Contains("--current-only", StringComparer.Ordinal);
    var dataverse = LoadDataverseOptions(appsettingsPath);
    await using var services = BuildServices(options, dataverse);
    var runner = services.GetRequiredService<SpoLocalProcessor>();
    // Chọn source được bật và sắp catalog cha trước reading.
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
        // Mỗi file được mở theo stream và đóng ngay sau khi xử lý xong source đó.
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

    // Ghi receipt local để người vận hành biết file nào đã giao, bỏ qua hoặc lỗi.
    var receiptDirectory = Path.Combine(Environment.CurrentDirectory, ".artifacts", "spo-local");
    Directory.CreateDirectory(receiptDirectory);
    var receiptPath = Path.Combine(receiptDirectory, $"receipt-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.json");
    await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(new { utcNow, results = ingestResults }, SpoConfiguration.Json));
    Console.WriteLine($"Receipt: {receiptPath}");
    return;
}
// Command còn lại duy nhất là preview-all: chỉ parse/map để kiểm tra, không ghi Dataverse.
if (args[0] != "preview-all") throw new InvalidOperationException("Unknown command. Use --help.");
await using var previewServices = BuildServices(options);
var previewer = previewServices.GetRequiredService<SpoPreviewer>();
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
    // Apply the same ignored per-machine override as the SQL worker. The SPO
    // consumer still uses the checked-in target organization and source IDs.
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    if (!document.RootElement.TryGetProperty("Dataverse", out var section))
        throw new InvalidOperationException($"Dataverse section is missing in '{path}'.");
    var merged = JsonNode.Parse(section.GetRawText())?.AsObject()
        ?? throw new InvalidOperationException($"Dataverse section is invalid in '{path}'.");
    var localPath = Path.Combine(Path.GetDirectoryName(path)!, "appsettings.Local.json");
    if (File.Exists(localPath))
    {
        using var local = JsonDocument.Parse(File.ReadAllText(localPath));
        if (local.RootElement.TryGetProperty("Dataverse", out var localSection))
        {
            var overrides = JsonNode.Parse(localSection.GetRawText())?.AsObject()
                ?? throw new InvalidOperationException($"Dataverse section is invalid in '{localPath}'.");
            foreach (var entry in overrides)
                merged[entry.Key] = entry.Value?.DeepClone();
        }
    }
    var options = JsonSerializer.Deserialize<SyncOptions>(merged.ToJsonString(), SpoConfiguration.Json)
        ?? throw new InvalidOperationException($"Dataverse section is invalid in '{path}'.");
    options.Validate();
    return options;
}

static ServiceProvider BuildServices(SpoIngestionOptions options, SyncOptions? dataverse = null)
{
    var services = new ServiceCollection();
    services.AddSpoProcessing(options);
    if (dataverse is not null)
    {
        services.AddSingleton(dataverse);
        services.AddSingleton<DataverseConnection>();
        // Connection sở hữu SDK client; các adapter chỉ mượn client.
        services.AddSingleton<ISpoBronzeWriter>(sp => new DataverseBronzeWriter(
            sp.GetRequiredService<DataverseConnection>().Get(), options));
        services.AddTransient(sp => new SpoDataverseInboxProcessor(
            sp.GetRequiredService<DataverseConnection>().Get(),
            sp.GetRequiredService<SpoLocalProcessor>(), options));
    }
    return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
}
