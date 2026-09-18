using BMS.Fake.Business.Abstractions;
using BMS.Fake.Business.Services;
using BMS.Fake.Common.Configuration;
using BMS.Fake.DataAccess.Services;
using Microsoft.Extensions.Configuration;

namespace BMS.Fake.App.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMetasysServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var intervalSeconds = configuration.GetValue("Simulator:IntervalSeconds", 3);
        services.AddSingleton(new SimulatorOptions(intervalSeconds));
        services.AddSingleton<IMetasysPointStore, MetasysPointStore>();
        services.AddSingleton<ISubscriptionManager, SubscriptionManager>();
        services.AddHostedService<MetasysSimulator>();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
