using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.PowerPlatform.Dataverse.Client;
using SPO.Ingestion.App.Hosting;
using SPO.Ingestion.Common;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        var configPath = Environment.GetEnvironmentVariable("SPO_CONFIG_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "config", "spo-ingestion.json");
        var options = SpoConfiguration.Load(configPath);
        var storage = Environment.GetEnvironmentVariable("SpoStorage")
            ?? throw new InvalidOperationException("SpoStorage app setting is required.");
        var dataverse = Environment.GetEnvironmentVariable("DataverseConnectionString")
            ?? throw new InvalidOperationException("DataverseConnectionString app setting is required.");
        services.AddSpoIngestionServices(options, storage, dataverse);
    })
    .Build();

await host.RunAsync();
