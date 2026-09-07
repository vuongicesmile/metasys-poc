using BmsIngestionApp.Models;
using BmsIngestionApp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var noSql = args.Contains("--no-sql", StringComparer.OrdinalIgnoreCase);
var webArgs = args.Where(arg => !arg.Equals("--no-sql", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(webArgs);
builder.WebHost.UseUrls("http://localhost:5200");
var settings = AppSettings.Load();

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(new IngestionRuntimeOptions(settings.Sql.Enabled && !noSql));
builder.Services.AddSingleton<IngestionStatusTracker>();
builder.Services.AddHostedService<CovIngestionWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "BMS Ingestion API v1");
    options.DocumentTitle = "BMS Ingestion App";
    options.DisplayRequestDuration();
});

app.MapGet("/", () => Results.Redirect("/swagger"))
    .ExcludeFromDescription();

app.MapGet("/api/ingestion/status", (IngestionStatusTracker status) => status.GetSnapshot())
    .WithTags("Ingestion")
    .WithSummary("Get COV ingestion and SQL persistence status")
    .Produces<IngestionStatusSnapshot>();

app.MapGet("/api/ingestion/events/recent", (IngestionStatusTracker status) => status.GetRecentEvents())
    .WithTags("Ingestion")
    .WithSummary("Get the 25 most recent COV events")
    .Produces<IReadOnlyList<CovEvent>>();

app.Run();

public partial class Program;
