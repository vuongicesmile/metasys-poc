using BMS.Fake.Business.Abstractions;
using BMS.Fake.Business.Services;
using BMS.Fake.Common.Configuration;
using BMS.Fake.DataAccess.Hosting;
using BMS.Fake.DataAccess.Services;
using Microsoft.Extensions.Configuration;

namespace BMS.Fake.App.Hosting;

/// <summary>Đăng ký dependency cho Fake Metasys application.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Đăng ký fixture store, subscription manager và simulator background service.</summary>
    public static IServiceCollection AddMetasysServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Đọc interval từ configuration, mặc định 3 giây.
        var intervalSeconds = configuration.GetValue("Simulator:IntervalSeconds", 3);
        // Đăng ký options dùng chung cho simulator.
        services.AddSingleton(new SimulatorOptions(intervalSeconds));
        // Một store duy nhất để API và simulator cùng thấy một state.
        // Đọc connection string của database riêng BMS_Fake.
        var connectionString = configuration["Database:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database:ConnectionString must be configured for BMS.Fake.");

        // Đăng ký options và toàn bộ EF Core data access.
        var databaseOptions = new FakeDatabaseOptions(connectionString);
        services.AddSingleton(databaseOptions);
        services.AddFakeDataAccess(databaseOptions);
        // Một subscription manager duy nhất để giữ các channel trong memory.
        services.AddSingleton<ISubscriptionManager, SubscriptionManager>();
        // Host tự khởi động background simulator.
        services.AddHostedService<MetasysSimulator>();
        // Bật metadata cho Swagger/OpenAPI.
        services.AddEndpointsApiExplorer();
        // Bật Swagger UI.
        services.AddSwaggerGen();
        // Trả collection để có thể chain registration.
        return services;
    }
}
