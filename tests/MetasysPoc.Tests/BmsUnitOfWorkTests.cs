using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Business.Contracts;
using BMS.Ingestion.DataAccess.Hosting;
using BMS.Ingestion.DataAccess.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MetasysPoc.Tests;

public sealed class BmsUnitOfWorkTests
{
    [Fact]
    public async Task Production_registration_resolves_isolated_units_without_connecting_to_sql()
    {
        var services = new ServiceCollection();
        services.AddBmsIngestionDataAccess("Server=localhost;Database=unused;Integrated Security=true;TrustServerCertificate=true");
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        { ValidateScopes = true, ValidateOnBuild = true });
        var factory = provider.GetRequiredService<IBmsIngestionUnitOfWorkFactory>();
        await using var first = await factory.CreateAsync();
        await using var second = await factory.CreateAsync();
        Assert.NotSame(first.Catalog, second.Catalog);
        Assert.NotSame(first.Readings, second.Readings);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IBmsCatalogRepository>());
    }

    [Fact]
    public async Task Repositories_stage_together_and_only_commit_saves_changes()
    {
        var options = new DbContextOptionsBuilder<BmsIngestionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new BmsIngestionDbContext(options);
        var catalog = new BMS.Ingestion.DataAccess.Services.BmsCatalogRepository(db);
        var readings = new BMS.Ingestion.DataAccess.Services.BmsReadingRepository(db);
        var unit = new BMS.Ingestion.DataAccess.Services.BmsIngestionUnitOfWork(db, catalog, readings);
        await unit.Catalog.StageAsync(new BmsCatalogDto(
            [new("B1", "Building", "Source", "")],
            [new("E1", "Equipment", "Sensor", "B1", "")]));
        unit.Readings.Add(new("P1", "Point", "Temperature", "B1", "E1", DateTime.UtcNow, 12.3456m, "C"));
        Assert.Equal(3, db.ChangeTracker.Entries().Count(e => e.State == EntityState.Added));
        await unit.CommitAsync();
        Assert.All(db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
        Assert.Equal(12.3456m, db.ChangeTracker.Entries().Single(e => e.Metadata.Name.EndsWith("BmsReadingEntity"))
            .Property("ReadingValue").CurrentValue);
    }
}
