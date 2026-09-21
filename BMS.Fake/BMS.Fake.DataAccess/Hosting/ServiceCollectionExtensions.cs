using BMS.Fake.Business.Abstractions;
using BMS.Fake.Common.Configuration;
using BMS.Fake.DataAccess.Persistence;
using BMS.Fake.DataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BMS.Fake.DataAccess.Hosting;

/// <summary>Đăng ký EF Core và các adapter database cho BMS Fake.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Đăng ký database factory, initializer và point store.</summary>
    public static IServiceCollection AddFakeDataAccess(
        this IServiceCollection services,
        FakeDatabaseOptions databaseOptions)
    {
        // Factory tạo DbContext ngắn hạn cho mỗi query/command.
        services.AddPooledDbContextFactory<FakeBmsDbContext>(options =>
            options.UseSqlServer(databaseOptions.ConnectionString));

        // Initializer được gọi một lần trước khi app nhận request.
        services.AddSingleton<FakeBmsDatabaseInitializer>();
        services.AddSingleton<IFakeUnitOfWorkFactory, FakeUnitOfWorkFactory>();

        // Point store là singleton về mặt service nhưng tạo DbContext theo từng method.
        services.AddSingleton<IMetasysPointStore, MetasysPointStore>();
        return services;
    }

    /// <summary>Khởi tạo database sau khi host đã tạo xong dependency graph.</summary>
    public static async Task InitializeFakeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        // Tạo scope để resolve initializer theo đúng lifetime của DI container.
        await using var scope = services.CreateAsyncScope();
        // Chạy EnsureCreated và seed trước khi app nhận HTTP request.
        await scope.ServiceProvider
            .GetRequiredService<FakeBmsDatabaseInitializer>()
            .InitializeAsync(cancellationToken);
    }
}
