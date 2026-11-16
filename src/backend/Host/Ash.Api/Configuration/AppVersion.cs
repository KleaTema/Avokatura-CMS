using System.Reflection;
using Ash.SharedKernel.Identity;

namespace Ash.Api.Configuration;

/// <summary>GET /api/version: the release (Ash:Version from the image, else the assembly's) and the environment.</summary>
public sealed record VersionResponse(string Version, string Environment);

/// <summary>
/// Task 052, item 9 — the running release: Ash__Version (set by the image from the release tag) or the assembly's
/// informational version (dotnet publish -p:InformationalVersion=X.Y.Z), without the "+commit" build metadata.
/// </summary>
public static class AppVersion
{
    public static string Current(IConfiguration configuration) =>
        configuration["Ash:Version"] is { Length: > 0 } configured
            ? configured
            : (typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
                .Split('+')[0];

    /// <summary>Authenticated (the fallback policy): the version is no secret, but nothing is given to anonymous callers.</summary>
    public static IEndpointRouteBuilder MapVersion(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/version", (IConfiguration configuration, IHostEnvironment environment, ICurrentUser _) =>
                Results.Ok(new VersionResponse(Current(configuration), environment.EnvironmentName)))
            .Produces<VersionResponse>()
            .WithName("GetVersion");
        return endpoints;
    }
}

/// <summary>
/// Task 052: one correlation id per request — nginx's X-Request-Id when it looks like one, else ASP.NET's own — in
/// every log line of the request (CorrelationId) and back in the response, so a user's error can be found in the logs.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Request-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[Header].ToString();
        var id = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? incoming
            : context.TraceIdentifier;
        context.Response.Headers[Header] = id;
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", id))
        {
            await next(context);
        }
    }
}
