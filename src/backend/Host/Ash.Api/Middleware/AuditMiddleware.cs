using Ash.Modules.Audit.Contracts;
using Ash.Modules.Audit.Infrastructure;

namespace Ash.Api.Middleware;

/// <summary>
/// Assigns one correlation id per request (returned as X-Correlation-Id), captures IP and user agent,
/// and records ACCESS_DENIED for 401/403 responses. Must wrap the authentication middleware.
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next)
{
    public const string CorrelationHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context, AuditRequestContext audit, IAuditLog auditLog)
    {
        var correlationId = Guid.NewGuid();
        audit.CorrelationId = correlationId;
        audit.IpAddress = context.Connection.RemoteIpAddress is { } ip
            ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()
            : null;
        audit.UserAgent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationHeader] = correlationId.ToString();
            return Task.CompletedTask;
        });

        await next(context);

        var status = context.Response.StatusCode;
        if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            auditLog.Record(
                AuditActions.AccessDenied,
                outcome: AuditOutcomes.Denied,
                details: new { method = context.Request.Method, path = context.Request.Path.Value, status });
        }
    }
}
