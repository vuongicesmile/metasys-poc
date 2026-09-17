using BMS.Ingestion.Common.Configuration;
using BMS.IngestionApp.Endpoints;
using BMS.IngestionApp.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

var noSql = args.Contains("--no-sql", StringComparer.OrdinalIgnoreCase);
var webArgs = args.Where(arg => !arg.Equals("--no-sql", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(webArgs);
builder.WebHost.UseUrls("http://localhost:5200");
var settings = AppSettings.Load();

builder.Services.AddIngestionServices(settings, !noSql);
var app = builder.Build();
app.MapIngestionEndpoints();
app.Run();

public partial class Program;
