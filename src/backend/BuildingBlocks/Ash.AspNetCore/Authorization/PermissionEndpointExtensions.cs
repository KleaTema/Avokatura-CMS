using Microsoft.AspNetCore.Builder;

namespace Ash.AspNetCore.Authorization;

public static class PermissionPolicy
{
    /// <summary>Policy names "perm:&lt;code&gt;" are resolved by the host's policy provider.</summary>
    public const string Prefix = "perm:";
}

public static class PermissionEndpointExtensions
{
    /// <summary>Requires an authenticated user holding the given system permission; otherwise 401 / 403.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permissionCode)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicy.Prefix + permissionCode);
}
