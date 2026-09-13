using FakeMetasysApi.Services;

namespace FakeMetasysApi.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMetasysServices(this IServiceCollection services)
    {
        services.AddSingleton<MetasysPointStore>();
        services.AddSingleton<SubscriptionManager>();
        services.AddHostedService<MetasysSimulator>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
