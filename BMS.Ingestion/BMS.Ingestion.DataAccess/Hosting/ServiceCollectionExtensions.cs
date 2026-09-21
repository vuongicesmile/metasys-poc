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
    /// Đăng ký pooled DbContext factory và hai repository có trách nhiệm riêng.
    /// Connection string chỉ được dùng khi repository thực hiện thao tác database.
    /// </summary>
    public static IServiceCollection AddBmsIngestionDataAccess(
        this IServiceCollection services,
        string connectionString)
    {
        // Đăng ký factory để mỗi thao tác có một DbContext ngắn hạn.
        services.AddPooledDbContextFactory<BmsIngestionDbContext>(options =>
            options.UseSqlServer(connectionString));
        // Repository catalog chỉ xử lý building và equipment.
        services.AddSingleton<IBmsCatalogRepository, BmsCatalogRepository>();
        // Repository reading chỉ xử lý append reading history.
        services.AddSingleton<IBmsReadingRepository, BmsReadingRepository>();
        // Trả lại IServiceCollection để có thể nối tiếp các đăng ký khác.
        return services;
    }
}
