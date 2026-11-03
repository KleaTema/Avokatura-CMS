using Microsoft.Extensions.DependencyInjection;

namespace Ash.Infrastructure.Scanning;

public static class ScanningServiceCollectionExtensions
{
    /// <summary>
    /// The virus scanner. NoOp (every file CLEAN) is allowed ONLY in Development: anywhere else the app refuses to
    /// start with it, like the dev authentication — an unscanned file must never reach production.
    /// </summary>
    public static IServiceCollection AddAshScanning(this IServiceCollection services, ScannerOptions options, bool isDevelopment)
    {
        var mode = options.Mode ?? (isDevelopment ? ScannerMode.NoOp : ScannerMode.ClamAv);
        if (mode == ScannerMode.NoOp && !isDevelopment)
        {
            throw new InvalidOperationException(
                "Scanner:Mode NoOp is allowed only in Development. Configure Scanner:Mode = ClamAv and Scanner:ClamAv:Host/Port.");
        }

        options.Mode = mode;
        services.AddSingleton(options);
        services.AddSingleton(options.ClamAv);
        services.AddSingleton(options.Job);
        if (mode == ScannerMode.NoOp)
        {
            services.AddSingleton<IVirusScanner, NoOpScanner>();
        }
        else
        {
            services.AddSingleton<IVirusScanner, ClamAvScanner>();
        }

        return services;
    }
}
