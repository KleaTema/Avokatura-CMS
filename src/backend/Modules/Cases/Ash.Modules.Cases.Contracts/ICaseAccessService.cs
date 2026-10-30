using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Contracts;

/// <summary>The single gate to case data: every case read or write is checked here.</summary>
public interface ICaseAccessService
{
    Task<bool> CanViewAsync(long caseId, CancellationToken cancellationToken = default);

    Task<bool> HasAsync(long caseId, string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="CaseAccessException"/>: NotFound when the user cannot view the case, Forbidden when they lack the permission.</summary>
    Task RequireAsync(long caseId, string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the current user may do in this case NOW: the case permissions (v_case_permissions) plus the system
    /// permissions that act on this case in its current state. Empty when the user cannot view the case.
    /// The one source of truth for actions: the case page shows it and <see cref="HasAsync"/>/<see cref="RequireAsync"/>
    /// check against it, so the UI and the API never differ. Cached per request.
    /// </summary>
    Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(long caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// For the registration lookup (task 018) ONLY: which cases Protokoll may FIND to continue at the next level.
    /// It never grants case.view — the lookup shows a few registration fields, nothing else of the case.
    /// </summary>
    RegistrationLookupScope GetRegistrationLookupScope();
}

public enum RegistrationLookupScope
{
    /// <summary>No system permission case.link_previous.</summary>
    None,

    /// <summary>Cases of every office: an appeal summons may arrive at another office than the first level.</summary>
    AllOffices
}

public sealed class CaseAccessException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}
