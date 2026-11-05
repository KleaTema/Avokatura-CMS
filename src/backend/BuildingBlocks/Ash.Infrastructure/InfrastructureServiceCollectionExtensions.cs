using Ash.Infrastructure.Events;
using Ash.Infrastructure.Persistence;
using Ash.SharedKernel.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <param name="queryTimeLimit">Read queries stop after this (default 5 s; "Database:QueryTimeLimitSeconds").</param>
    public static IServiceCollection AddAshInfrastructure(this IServiceCollection services, string connectionString, TimeSpan? queryTimeLimit = null)
    {
        services.AddSingleton(new PersistenceOptions(connectionString, queryTimeLimit ?? PersistenceOptions.DefaultQueryTimeLimit));
        services.AddScoped<MySqlUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MySqlUnitOfWork>());
        services.AddScoped<InheritedCurrentUser>();
        services.AddScoped<ParallelReads>();
        services.AddScoped<IEventPublisher, InProcessEventPublisher>();
        return services;
    }
}
