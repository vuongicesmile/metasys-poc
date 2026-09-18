using DataverseSyncWorker.Hosting;
using DataverseSyncWorker.Endpoints;

var commandLine = WorkerCommandLine.Parse(args);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = commandLine.HostArgs,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Host.UseWindowsService(options => options.ServiceName = "FMCentralDataverseSync");
builder.Services.AddDataverseSync(builder.Configuration);
var app = builder.Build();

// Maintenance commands exit before the hosted worker can run.
if (await app.Services.GetRequiredService<WorkerCommandDispatcher>().Execute(commandLine)) return;

app.MapSyncEndpoints();
await app.RunAsync();
