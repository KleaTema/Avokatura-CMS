namespace Ash.Api.Security;

/// <summary>
/// Task 051 — the API's response headers, on every response:
/// <list type="bullet">
/// <item>X-Content-Type-Options: nosniff — a JSON or file answer is never re-interpreted as HTML / script;</item>
/// <item>Referrer-Policy: no-referrer; Permissions-Policy: camera, microphone, geolocation off;</item>
/// <item>Content-Security-Policy: default-src 'none'; frame-ancestors 'none' — the API serves data, never pages to run
/// or frame (the Development-only API pages under /scalar are left out);</item>
/// <item>Cache-Control: no-store on /api — case data never lands in a shared or browser cache.</item>
/// </list>
/// HSTS (only over HTTPS, never in Development): UseHsts in Program.cs.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ApiCsp = "default-src 'none'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            // an endpoint's own policy wins (the document preview's sandbox, task 035) — it is stricter for that answer
            if (!context.Request.Path.StartsWithSegments("/scalar") && string.IsNullOrEmpty(headers.ContentSecurityPolicy))
            {
                headers.ContentSecurityPolicy = ApiCsp;
            }

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
                headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });
        return next(context);
    }
}

/// <summary>
/// Task 051 — CORS: only the configured frontend origin(s) (Cors:AllowedOrigins). In production the frontend and the
/// API share one origin behind the reverse proxy, so the list is empty and no CORS header is ever sent.
/// </summary>
public static class CorsSetup
{
    public const string PolicyName = "frontend";

    public static string[] Origins(IConfiguration configuration) =>
        configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.Where(o => !string.IsNullOrWhiteSpace(o)).ToArray() ?? [];

    public static IServiceCollection AddAshCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = Origins(configuration);
        foreach (var origin in origins)
        {
            if (origin.Contains('*', StringComparison.Ordinal) || !Uri.TryCreate(origin, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException($"Cors:AllowedOrigins: \"{origin}\" must be an exact origin (no wildcard).");
            }
        }

        services.AddCors(o => o.AddPolicy(PolicyName, p => p
            .WithOrigins(origins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
            .WithHeaders("Authorization", "Content-Type")
            .AllowCredentials()));
        return services;
    }
}
