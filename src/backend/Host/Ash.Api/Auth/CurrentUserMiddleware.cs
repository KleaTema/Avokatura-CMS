using System.Security.Claims;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Audit.Infrastructure;
using Ash.Modules.Identity.Application.Auth;
using Ash.Modules.Identity.Application.ResolveUser;
using Ash.SharedKernel.Identity;

namespace Ash.Api.Auth;

/// <summary>
/// Maps the authenticated principal to a users row and fills ICurrentUser; an unknown or inactive user → 401 (logged,
/// never auto-created). A provider's token (task 051): its claim map gives the subject and the e-mail, and the user is
/// found by (provider, subject) — the first login links by e-mail. The dev header (Development only): by e-mail.
/// </summary>
public sealed class CurrentUserMiddleware(RequestDelegate next, ILogger<CurrentUserMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context, ResolveUserHandler handler, CurrentUser currentUser, AuditRequestContext audit, AuthProvidersOptions providers)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        ResolveUserRequest request;
        var providerName = context.User.FindFirst(AuthExtensions.ProviderClaim)?.Value;
        if (providerName is not null && providers.Enabled.FirstOrDefault(p => p.Name == providerName) is { } provider)
        {
            var mapped = IdentityClaimMap.Map(context.User.Claims, provider);
            if (mapped.EmailFromUsername)
            {
                logger.LogWarning(
                    "Provider {Provider}: no e-mail claim, the user name was used as the e-mail — configure the optional claim \"email\"",
                    provider.Name);
            }

            request = new ResolveUserRequest(provider.Name, mapped.Subject, mapped.Email);
        }
        else
        {
            request = new ResolveUserRequest(null, null, context.User.FindFirst(DevAuthHandler.EmailClaim)?.Value);
        }

        var result = await handler.Handle(request, context.RequestAborted);
        if (result.IsFailure)
        {
            // never the token, never the e-mail in full: the provider and whether a subject was present
            logger.LogWarning("Sign-in refused: no active user for provider {Provider} (subject present: {HasSubject})",
                request.Provider ?? "dev", !string.IsNullOrEmpty(request.Subject));
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        currentUser.Set(result.Value);
        // the SignalR user id (PublicIdUserIdProvider): the resolved users row, not whatever the token names
        context.User.AddIdentity(new ClaimsIdentity([new Claim(AshClaims.UserPublicId, result.Value.PublicId.ToString())]));
        audit.Actor = new AuditActor(result.Value.UserId, result.Value.DisplayName, string.Join(',', result.Value.Roles));
        await next(context);
    }
}
