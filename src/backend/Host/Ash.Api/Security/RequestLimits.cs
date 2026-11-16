using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;

namespace Ash.Api.Security;

/// <summary>
/// Task 051, item 6 — request limits:
/// <list type="bullet">
/// <item>every request body at most 1 MB, except the document upload (its own limit from the allowed file types, task
/// 033 — the handler raises it) and the Organika import (3 MB: a sheet of at most 2 MB plus the form);</item>
/// <item>a Content-Length above the limit is refused at once (413 ProblemDetails) — a body without a length is cut by
/// the server's own per-request limit while it is read;</item>
/// <item>JSON at most 32 levels deep; multipart: at most 16 headers per section, a boundary of at most 70 characters;</item>
/// <item>a JSON endpoint given another content type answers 415 (minimal APIs do it by themselves).</item>
/// </list>
/// </summary>
public static class RequestLimits
{
    public const long DefaultMaxBodyBytes = 1L * 1024 * 1024;
    public const long OrganikaMaxBodyBytes = 3L * 1024 * 1024;
    public const int MultipartHeadersCountLimit = 16;
    public const int MultipartBoundaryLengthLimit = 70;
    public const int JsonMaxDepth = 32;
    public const string Message = "Kërkesa është shumë e madhe.";

    /// <summary>The body limit of a request by its route (null = the endpoint sets its own: the document upload).</summary>
    public static long? LimitOf(HttpContext context)
    {
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? string.Empty;
        return (context.Request.Method, route) switch
        {
            ("POST", "/api/cases/{id:guid}/documents") => null,
            ("POST", "/api/identity/admin/organika/imports") => OrganikaMaxBodyBytes,
            _ => DefaultMaxBodyBytes,
        };
    }

    public static IServiceCollection AddAshRequestLimits(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.MaxDepth = JsonMaxDepth);
        services.Configure<FormOptions>(o =>
        {
            o.MultipartHeadersCountLimit = MultipartHeadersCountLimit;
            o.MultipartBoundaryLengthLimit = MultipartBoundaryLengthLimit;
            o.MultipartBodyLengthLimit = OrganikaMaxBodyBytes;
            o.ValueCountLimit = 100;
        });
        return services;
    }
}

/// <summary>Applies <see cref="RequestLimits.LimitOf"/> to every request (see <see cref="RequestLimits"/>).</summary>
public sealed class RequestLimitsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var limit = RequestLimits.LimitOf(context);
        if (limit is { } max)
        {
            if (context.Request.ContentLength > max)
            {
                await Results.Problem(title: RequestLimits.Message, statusCode: StatusCodes.Status413PayloadTooLarge,
                        extensions: new Dictionary<string, object?> { ["code"] = "request.too_large" })
                    .ExecuteAsync(context);
                return;
            }

            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            {
                feature.MaxRequestBodySize = max;
            }
        }

        await next(context);
    }
}
