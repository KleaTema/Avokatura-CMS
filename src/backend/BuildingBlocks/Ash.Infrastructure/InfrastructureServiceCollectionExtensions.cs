using Ash.Infrastructure.Events;
using Ash.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAshInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new PersistenceOptions(connectionString));
        services.AddScoped<MySqlUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MySqlUnitOfWork>());
        services.AddScoped<IEventPublisher, InProcessEventPublisher>();
        return services;
    }
}
