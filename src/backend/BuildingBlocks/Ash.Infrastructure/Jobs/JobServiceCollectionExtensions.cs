using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Infrastructure.Jobs;

public static class JobServiceCollectionExtensions
{
    /// <summary>The scheduler, the runner and the "jobs" health check's parts (task 041). Jobs: AddScheduledJob.</summary>
    public static IServiceCollection AddAshJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection(JobOptions.Section).Get<JobOptions>() ?? new JobOptions());
        services.AddSingleton<JobSchedulerState>();
        services.AddSingleton<JobRunner>();
        services.AddSingleton<JobsHealthCheck>();
        services.AddScoped<SentReminders>();
        services.AddHostedService<JobScheduler>();
        return services;
    }

    /// <summary>A job of a module; resolved in a fresh scope for every run.</summary>
    public static IServiceCollection AddScheduledJob<TJob>(this IServiceCollection services)
        where TJob : class, IScheduledJob
    {
        services.AddScoped<TJob>();
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<TJob>());
        return services;
    }
}
