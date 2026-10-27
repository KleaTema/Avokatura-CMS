using Ash.SharedKernel.Identity;

namespace Ash.Modules.Identity.Application.ResolveUser;

/// <summary>Scoped holder filled by the API's CurrentUserMiddleware.</summary>
public sealed class CurrentUser : ICurrentUser
{
    private ResolveUserResponse? _user;

    public void Set(ResolveUserResponse user) => _user = user;

    private ResolveUserResponse User =>
        _user ?? throw new InvalidOperationException("No authenticated user for this request.");

    public long UserId => User.UserId;
    public Guid PublicId => User.PublicId;
    public string DisplayName => User.DisplayName;
    public long? OfficeId => User.OfficeId;
    public long? InstitutionId => User.InstitutionId;
    public IReadOnlyCollection<string> Roles => User.Roles;
    public IReadOnlyCollection<string> Permissions => User.Permissions;
    public bool HasPermission(string permission) => User.Permissions.Contains(permission);
}
