using Ash.SharedKernel.Identity;

namespace Ash.Modules.Cases.Application.ListCases;

public enum CaseListTab
{
    /// <summary>"Çështjet e mia": a current assignment of the user (lawyers, assistants, institution jurists).</summary>
    Mine,

    /// <summary>REGISTERED cases registered by the user (Protokoll).</summary>
    Registered,

    /// <summary>"Pa avokat": REGISTERED cases of the Kryetar's office.</summary>
    Unassigned,

    /// <summary>"Të zyrës": all cases of the Kryetar's office.</summary>
    Office,

    /// <summary>"Të institucionit": cases with an active link to the user's institution.</summary>
    Institution,

    /// <summary>"Të gjitha": every case (Monitor, Admin).</summary>
    All
}

/// <summary>Which tabs a user gets, decided by permissions and the kind of user — never by a role name alone.</summary>
public static class CaseListTabs
{
    public const string CreatePermission = "case.create";
    public const string ViewOfficePermission = "case.view_office";
    public const string ViewAllPermission = "case.view_all";

    /// <summary>Roles that hold case assignments, so the user has "my cases".</summary>
    private static readonly string[] AssignableRoles = ["STATE_LAWYER", "LEGAL_ASSISTANT", "INSTITUTION_JURIST"];
    private const string InstitutionUserRole = "INSTITUTION_USER";

    public static string Code(CaseListTab tab) => tab switch
    {
        CaseListTab.Mine => "mine",
        CaseListTab.Registered => "registered",
        CaseListTab.Unassigned => "unassigned",
        CaseListTab.Office => "office",
        CaseListTab.Institution => "institution",
        CaseListTab.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(tab))
    };

    public static bool TryParse(string? code, out CaseListTab tab)
    {
        foreach (var candidate in Enum.GetValues<CaseListTab>())
        {
            if (string.Equals(Code(candidate), code?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                tab = candidate;
                return true;
            }
        }

        tab = default;
        return false;
    }

    /// <summary>The tabs of this user, in display order.</summary>
    public static IReadOnlyList<CaseListTab> For(ICurrentUser user)
    {
        var tabs = new List<CaseListTab>();
        if (user.Roles.Any(r => AssignableRoles.Contains(r)))
        {
            tabs.Add(CaseListTab.Mine);
        }

        if (user.HasPermission(CreatePermission) && user.OfficeId is not null)
        {
            tabs.Add(CaseListTab.Registered);
        }

        if (user.HasPermission(ViewOfficePermission) && user.OfficeId is not null)
        {
            tabs.Add(CaseListTab.Unassigned);
            tabs.Add(CaseListTab.Office);
        }

        if (user.Roles.Contains(InstitutionUserRole) && user.InstitutionId is not null)
        {
            tabs.Add(CaseListTab.Institution);
        }

        if (user.HasPermission(ViewAllPermission))
        {
            tabs.Add(CaseListTab.All);
        }

        return tabs;
    }

    /// <summary>
    /// Monitor/Admin → all; Kryetar → unassigned when it has cases, else office; institution user → institution;
    /// Protokoll → registered; lawyer → mine.
    /// </summary>
    public static CaseListTab? Default(IReadOnlyList<CaseListTab> tabs, int unassignedCount)
    {
        if (tabs.Contains(CaseListTab.All))
        {
            return CaseListTab.All;
        }

        if (tabs.Contains(CaseListTab.Unassigned))
        {
            return unassignedCount > 0 ? CaseListTab.Unassigned : CaseListTab.Office;
        }

        foreach (var tab in new[] { CaseListTab.Institution, CaseListTab.Registered, CaseListTab.Mine })
        {
            if (tabs.Contains(tab))
            {
                return tab;
            }
        }

        return null;
    }
}
