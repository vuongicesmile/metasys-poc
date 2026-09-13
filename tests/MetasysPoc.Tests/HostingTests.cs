using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.Hosting;
using DataverseSyncWorker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MetasysPoc.Tests;

public sealed class HostingTests
{
    [Fact]
    public void Worker_and_endpoint_use_the_same_sync_gate_instance()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:Sql"] = "Server=localhost;Database=test;Integrated Security=true;TrustServerCertificate=true"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDataverseSync(configuration);
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<SyncEngine>(), provider.GetRequiredService<ISyncEngine>());
        Assert.Same(provider.GetRequiredService<SqlStore>(), provider.GetRequiredService<ISyncLedger>());
        Assert.Same(provider.GetRequiredService<CommandProcessor>(), provider.GetRequiredService<ICommandProcessor>());
    }

    [Theory]
    [InlineData("--self-test")]
    [InlineData("--run-once")]
    [InlineData("--verify")]
    [InlineData("--process-command-once")]
    public void Maintenance_flags_are_removed_from_host_configuration(string flag)
    {
        var result = WorkerCommandLine.Parse([flag, "--urls", "http://localhost:5301", "--plugin-path=C:/plugin.dll"]);
        Assert.Contains(flag, result.CommandArgs);
        Assert.Equal(new[] { "--urls", "http://localhost:5301" }, result.HostArgs);
    }
}
