namespace Ash.Modules.Identity.Contracts;

/// <summary>System-scope permissions of the current user.</summary>
public interface IPermissionService
{
    bool Has(string permissionCode);
}
