using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.DataAccess.Persistence;
using BMS.Ingestion.DataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BMS.Ingestion.DataAccess.Hosting;

/// <summary>Đăng ký adapter persistence EF Core cho ingestion service.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký pooled DbContext, repositories, Unit of Work và factory dùng bởi worker.
    /// </summary>
    public static IServiceCollection AddBmsIngestionDataAccess(
        this IServiceCollection services,
        string connectionString)
    {
        // Mỗi DI scope có một DbContext; dispose scope sẽ trả context về pool.
        services.AddDbContextPool<BmsIngestionDbContext>(options =>
            options.UseSqlServer(connectionString));
        // Các repository scoped dùng chung DbContext của Unit of Work.
        services.AddScoped<IBmsCatalogRepository, BmsCatalogRepository>();
        services.AddScoped<IBmsReadingRepository, BmsReadingRepository>();
        services.AddScoped<IBmsIngestionUnitOfWork, BmsIngestionUnitOfWork>();
        // BackgroundService là singleton nên chỉ nhận factory an toàn về lifetime.
        services.AddSingleton<IBmsIngestionUnitOfWorkFactory, BmsIngestionUnitOfWorkFactory>();
        // Trả lại IServiceCollection để có thể nối tiếp các đăng ký khác.
        return services;
    }
}
