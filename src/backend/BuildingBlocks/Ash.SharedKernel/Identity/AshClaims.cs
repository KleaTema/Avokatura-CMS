namespace Ash.SharedKernel.Identity;

/// <summary>Claims the API adds to the authenticated principal after resolving the users row.</summary>
public static class AshClaims
{
    /// <summary>The resolved user's public id (e.g. the SignalR user id). Added by CurrentUserMiddleware.</summary>
    public const string UserPublicId = "ash_user_public_id";
}
