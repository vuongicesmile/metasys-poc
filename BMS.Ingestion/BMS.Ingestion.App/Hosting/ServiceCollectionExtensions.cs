using BMS.Ingestion.Business.Abstractions;
using BMS.Ingestion.Business.Services;
using BMS.Ingestion.Common.Configuration;
using BMS.Ingestion.DataAccess.Hosting;
using BMS.Ingestion.DataAccess.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BMS.Ingestion.App.Hosting;

/// <summary>Các đăng ký dependency tại composition root của BMS ingestion app.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Đăng ký source client, business service và persistence SQL tùy chọn.</summary>
    public static IServiceCollection AddIngestionServices(this IServiceCollection services, AppSettings settings, bool sqlEnabled = true)
    {
        // Đăng ký toàn bộ app settings dùng chung.
        services.AddSingleton(settings);
        // SQL chỉ bật khi cả cấu hình và tham số runtime đều cho phép.
        services.AddSingleton(new IngestionRuntimeOptions(settings.Sql.Enabled && sqlEnabled));
        // Status tracker dùng chung cho worker và các endpoint status.
        services.AddSingleton<IngestionStatusTracker>();
        // Đăng ký DbContext factory cùng hai repository EF Core.
        services.AddBmsIngestionDataAccess(settings.Sql.ConnectionString);
        // Tạo HttpClient có base URL trỏ tới Fake Metasys.
        services.AddHttpClient(MetasysClient.ClientName, client =>
        {
            // Các endpoint client gọi sẽ dùng BaseAddress này.
            client.BaseAddress = new Uri(settings.Metasys.BaseUrl, UriKind.Absolute);
            // SSE stream có thể chạy lâu nên không dùng timeout mặc định.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        // Đăng ký implementation của source client qua interface business.
        services.AddSingleton<IMetasysClient, MetasysClient>();
        // Host tự tạo và chạy BackgroundService này.
        services.AddHostedService<CovIngestionWorker>();
        // Bật metadata cần cho Swagger/OpenAPI.
        services.AddEndpointsApiExplorer();
        // Bật Swagger UI cho app.
        services.AddSwaggerGen();
        // Trả collection để caller có thể chain thêm registration.
        return services;
    }
}
