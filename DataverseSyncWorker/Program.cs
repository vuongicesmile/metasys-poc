using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;

var (relationCommand, remainingArgs) = BmsRelationCommand.Parse(args);
args = remainingArgs;
var commands = new[] { "--provision", "--run-once", "--self-test", "--verify", "--enqueue", "--process-command-once" };
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args.Where(a => !commands.Contains(a)).ToArray(),
    ContentRootPath = AppContext.BaseDirectory
});
builder.Host.UseWindowsService(options => options.ServiceName = "FMCentralDataverseSync");
var options = builder.Configuration.GetSection("Dataverse").Get<SyncOptions>() ?? new();
options.Validate();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ReadingMapper>();
builder.Services.AddSingleton<SqlStore>();
builder.Services.AddSingleton<DataverseConnection>();
builder.Services.AddSingleton<IDataverseWriter, DataverseWriter>();
builder.Services.AddSingleton<SyncEngine>();
builder.Services.AddSingleton<SyncRequestStore>();
builder.Services.AddSingleton<ISyncRequestStore>(services => services.GetRequiredService<SyncRequestStore>());
builder.Services.AddSingleton<CommandProcessor>();
builder.Services.AddSingleton<RuntimeState>();
builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();

// Maintenance exits before app.RunAsync: it never starts the hosted sync worker.
if (relationCommand is not null)
{
    if (relationCommand.Mode == "--self-test-bms-relations")
    {
        await BmsRelationshipVerification.SelfTest();
        return;
    }
    var manifest = BmsRelationManifest.Load(relationCommand.ManifestPath, options.SourceId);
    var connection = app.Services.GetRequiredService<DataverseConnection>();
    var provisioner = new DataverseProvisioner(connection, options);
    if (relationCommand.Mode == "--bms-relations-status")
        await provisioner.PrintBmsRelationStatus(manifest);
    else if (relationCommand.Mode == "--provision-bms-relations")
    {
        await provisioner.PrintBmsRelationStatus(manifest);
        await provisioner.ProvisionBmsRelations();
    }
    else
    {
        await provisioner.ReadBmsRelationMetadata(true);
        var seeder = new BmsRelationshipSeeder(new DataverseBmsRelationStore(connection.Get()), options);
        if (relationCommand.Mode == "--verify-bms-relations")
        {
            await provisioner.VerifyBmsRelationUi();
            await Verification.VerifyRelationships(app.Services);
        }
        else
        {
            var plan = await seeder.Plan(manifest);
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(plan, BmsRelationManifest.Json));
            if (plan.Errors.Length > 0) throw new InvalidOperationException("Seed preflight failed; no writes made.");
            if (relationCommand.Apply)
            {
                var receiptPath = relationCommand.ReceiptPath ?? Path.Combine(Environment.CurrentDirectory,
                    ".artifacts", "bms-relations", "receipts", $"seed-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
                var receipt = await seeder.Apply(manifest, receiptPath);
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(receipt, BmsRelationManifest.Json));
            }
            else Console.WriteLine("DRY RUN: no cloud writes. Use --apply to apply this manifest.");
        }
    }
    return;
}

if (args.Contains("--self-test")) { await Verification.SelfTest(app.Services); return; }
if (args.Contains("--provision")) { await new DataverseProvisioner(app.Services.GetRequiredService<DataverseConnection>(), options).Run(); return; }
if (args.Contains("--verify")) { await Verification.Reconcile(app.Services); return; }
if (args.Contains("--enqueue"))
{
    if (!options.HasCredentials) throw new InvalidOperationException("Dataverse credentials required.");
    var queued = await app.Services.GetRequiredService<SyncRequestStore>().Enqueue(Environment.UserName, CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { requestId = queued.Id, created = queued.Created }));
    return;
}
if (args.Contains("--process-command-once"))
{
    if (!options.HasCredentials) throw new InvalidOperationException("Dataverse credentials required.");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        await app.Services.GetRequiredService<CommandProcessor>().TryRun(CancellationToken.None)));
    return;
}
if (args.Contains("--run-once"))
{
    if (!options.HasCredentials) throw new InvalidOperationException("Dataverse credentials required.");
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await app.Services.GetRequiredService<SyncEngine>().Run(CancellationToken.None)));
    return;
}

app.UseSwagger();
app.UseSwaggerUI(o => { o.SwaggerEndpoint("/swagger/v1/swagger.json", "FM Central Dataverse Sync v1"); o.DocumentTitle = "Dataverse Sync Worker"; });
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/api/dataverse-sync/status", async (RuntimeState state, SqlStore sql, CancellationToken ct) =>
{
    try { return Results.Ok(new SyncStatusResponse(state.Snapshot, await sql.Summary(ct), options.Url, options.HistoryEnabled)); }
    catch (Microsoft.Data.SqlClient.SqlException) { return Results.Problem("SQL integration schema is unavailable. Run create-dataverse-sync-tables.sql."); }
}).WithTags("Sync").WithSummary("Runtime state, SQL pending rows and acknowledged checkpoint")
    .Produces<SyncStatusResponse>().ProducesProblem(500);
app.MapGet("/api/dataverse-sync/dead-letters", (SqlStore sql, CancellationToken ct) => sql.DeadLetters(ct))
    .WithTags("Sync").WithSummary("Up to 100 quarantined SQL readings");
if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/dataverse-sync/run-once", async (SyncEngine engine, CancellationToken ct) =>
        !options.HasCredentials ? Results.Problem("Configure Dataverse credentials first.", statusCode: 503) : Results.Ok(await engine.Run(ct)))
        .WithTags("Development").WithSummary("Process one batch (serialized with background worker)");
    app.MapPost("/api/dataverse-sync/dead-letters/{id:long}/replay", async (long id, SqlStore sql, CancellationToken ct) =>
        Results.Ok(new { released = await sql.Replay(id, ct) }))
        .WithTags("Development").WithSummary("Release a corrected row for the next synchronization run");
}
await app.RunAsync();
