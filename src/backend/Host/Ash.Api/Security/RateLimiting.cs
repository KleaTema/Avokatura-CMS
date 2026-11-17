using System.Threading.RateLimiting;
using Ash.SharedKernel.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace Ash.Api.Security;

/// <summary>One rate limit: so many requests per window, per user (anonymous: per IP address).</summary>
public sealed record RateLimitRule(int PermitLimit, int WindowSeconds);

/// <summary>
/// Task 051, item 5 — rate limits in ONE place (ASP.NET's RateLimiter as a global limiter keyed by the matched route,
/// so no endpoint has to carry an attribute). Per user (the resolved users row), an anonymous request per IP; over the
/// limit → 429 ProblemDetails "Shumë kërkesa. Provoni pas pak." with Retry-After. Limits from RateLimits:{policy}
/// (PermitLimit, WindowSeconds); the defaults below. The chat's own send limit (30/minute, task 049) stays as it is.
/// </summary>
public static class RateLimiting
{
    public const string Upload = "upload";
    public const string Export = "export";
    public const string Search = "search";
    /// <summary>Cross-office lookups of the registration desk (client decision Q1a) — the strictest.</summary>
    public const string CrossOffice = "crossOffice";
    public const string Notify = "notify";
    public const string Hub = "hub";

    public static readonly IReadOnlyDictionary<string, RateLimitRule> Defaults = new Dictionary<string, RateLimitRule>
    {
        [Upload] = new(30, 60),
        [Export] = new(10, 60),
        [Search] = new(60, 60),
        [CrossOffice] = new(20, 60),
        [Notify] = new(5, 60),
        [Hub] = new(20, 60),
    };

    public const string Message = "Shumë kërkesa. Provoni pas pak.";

    /// <summary>Which policy a request falls under (null = not limited here), by method and route pattern.</summary>
    public static string? PolicyOf(HttpContext context)
    {
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;
        var method = context.Request.Method;
        if (route.StartsWith("/hubs/", StringComparison.Ordinal))
        {
            return route.EndsWith("/negotiate", StringComparison.Ordinal) ? Hub : null;
        }

        return (method, route) switch
        {
            ("POST", "/api/cases/{id:guid}/documents") => Upload,
            ("POST", "/api/identity/admin/organika/imports") => Upload,
            ("GET", _) when route.EndsWith("/export", StringComparison.Ordinal) => Export,
            ("GET", "/api/search") => Search,
            // the case list only when it searches a text
            ("GET", "/api/cases") when !string.IsNullOrWhiteSpace(context.Request.Query["q"]) => Search,
            ("GET", "/api/cases/registration-search") => CrossOffice,
            ("GET", "/api/cases/{previousCasePublicId:guid}/next-level/preview") => CrossOffice,
            ("POST", "/api/cases/{previousCasePublicId:guid}/notify-not-closed") => Notify,
            _ => null,
        };
    }

    public static IServiceCollection AddAshRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var rules = Defaults.ToDictionary(
            d => d.Key,
            d => configuration.GetSection($"RateLimits:{d.Key}").Get<RateLimitRule>() is { PermitLimit: > 0, WindowSeconds: > 0 } r ? r : d.Value);

        services.AddRateLimiter(o =>
        {
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var policy = PolicyOf(context);
                if (policy is null)
                {
                    return RateLimitPartition.GetNoLimiter(string.Empty);
                }

                var who = context.User.FindFirst(AshClaims.UserPublicId)?.Value
                          ?? $"ip:{context.Connection.RemoteIpAddress}";
                var rule = rules[policy];
                return RateLimitPartition.GetFixedWindowLimiter($"{policy}:{who}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rule.PermitLimit,
                    Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                    QueueLimit = 0,
                });
            });
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await Results.Problem(title: Message, statusCode: StatusCodes.Status429TooManyRequests,
                        extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }
}
