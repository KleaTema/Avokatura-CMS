namespace Ash.Modules.Identity.Contracts;

/// <summary>The users the administrator's dashboard counts (task 053n), each a filter of the users list.</summary>
public enum AdminUserCount
{
    Active,
    Inactive,
    NoRole,
    NeverLoggedIn,
    /// <summary>Created on or after the given day (Tirane).</summary>
    CreatedSince,
    /// <summary>With a blocked sign-in account.</summary>
    LoginBlocked,
    /// <summary>Not linked to any sign-in account yet.</summary>
    LoginNone,
}

/// <summary>An import whose preview / result has errors. Kind: ORGANIKA | USERS.</summary>
public sealed record AdminImportError(Guid PublicId, string Kind, string FileName, DateTime UploadedAt, int Errors);

/// <summary>
/// Task 053n — the numbers of the administrator's dashboard, computed by the SAME queries as the admin pages the cards
/// open (the users list's filters, the offices' head, the courts an office covers, the import history), so a card and
/// its list always agree. Nothing here depends on the administrator's office.
/// </summary>
public interface IAdminOverview
{
    Task<int> CountUsersAsync(AdminUserCount which, DateOnly since, CancellationToken ct = default);

    /// <summary>Active offices without a Kryetar (offices.head_user_id NULL).</summary>
    Task<int> CountOfficesWithoutHeadAsync(CancellationToken ct = default);

    /// <summary>The courts an active office covers (office_courts, the routing of new cases).</summary>
    Task<IReadOnlySet<int>> ListCoveredCourtIdsAsync(CancellationToken ct = default);

    /// <summary>The imports with errors among each kind's history (its last 100, as the page lists them), newest first.</summary>
    Task<IReadOnlyList<AdminImportError>> ListImportsWithErrorsAsync(CancellationToken ct = default);
}
