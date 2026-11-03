using Microsoft.Extensions.DependencyInjection;

namespace Ash.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>One <see cref="IFileStorage"/> for the whole app (the S3 client is thread-safe and pools connections).</summary>
    public static IServiceCollection AddAshStorage(this IServiceCollection services, StorageOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<S3FileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
        return services;
    }
}
