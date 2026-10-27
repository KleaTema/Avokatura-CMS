using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;

namespace Ash.Modules.Identity.Application;

public sealed class PermissionService(ICurrentUser user) : IPermissionService
{
    public bool Has(string permissionCode) => user.HasPermission(permissionCode);
}
