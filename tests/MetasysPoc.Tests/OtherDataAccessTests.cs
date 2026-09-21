using BMS.Fake.DataAccess.Persistence;
using BMS.Fake.DataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SPO.Ingestion.Business.Abstractions;
using SPO.Ingestion.DataAccess;
using SPO.Ingestion.DataAccess.Hosting;
using SPO.Ingestion.Domain;

namespace MetasysPoc.Tests;

public sealed class OtherDataAccessTests
{
    [Fact]
    public async Task Spo_job_interface_resolves_the_same_store_as_capture_endpoints()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SpoIngestionOptions());
        services.AddSpoDataAccess("UseDevelopmentStorage=true", "unused");
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        { ValidateScopes = true, ValidateOnBuild = true });
        Assert.Same(provider.GetRequiredService<BlobJobStore>(), provider.GetRequiredService<ISpoJobStore>());
    }

    [Fact]
    public async Task Fake_unit_of_work_discard_leaves_persisted_point_unchanged()
    {
        var options = new DbContextOptionsBuilder<FakeBmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var contexts = new PooledDbContextFactory<FakeBmsDbContext>(options);
        await new FakeBmsDatabaseInitializer(contexts).InitializeAsync();
        var factory = new FakeUnitOfWorkFactory(contexts);
        await using (var unit = await factory.CreateAsync())
        {
            var type = unit.Context.Model.GetEntityTypes().Single(t => t.ClrType.Name == "FakePointEntity").ClrType;
            var point = await unit.Context.FindAsync(type, "WATER-001");
            Assert.NotNull(point);
            unit.Context.Entry(point!).Property("Value").CurrentValue = 999m;
        }
        var store = new MetasysPointStore(contexts, factory);
        Assert.Equal(350m, (await store.GetAsync("WATER-001"))!.Value);
    }
}
