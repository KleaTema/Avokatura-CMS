using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Cases.Application.ListCases;

/// <summary>
/// Builds a <see cref="CaseListSpecialFilter"/> for the current user — the ONE path for the list endpoint and the
/// dashboard, so both count the same. Null when the filter needs an institution the user does not have.
/// </summary>
public sealed class CaseListSpecialFactory(IUserDirectory users, ICurrentUser user)
{
    public const string JuristRole = "INSTITUTION_JURIST";

    public async Task<CaseListSpecialFilter?> CreateAsync(CaseListSpecial kind, DateTime nowUtc, CancellationToken ct)
    {
        if (!CaseListSpecialFilter.NeedsInstitution(kind))
        {
            return CaseListSpecialFilter.Create(kind, nowUtc);
        }

        if (user.InstitutionId is not { } institution)
        {
            return null;
        }

        var jurists = kind == CaseListSpecial.NoJurist
            ? (await users.GetInstitutionUsersByRoleAsync(institution, JuristRole, ct)).Select(u => (uint)u.Id).ToList()
            : null;
        return CaseListSpecialFilter.Create(kind, nowUtc, (uint)institution, jurists);
    }
}

/// <summary>
/// The "filter=" of GET /api/cases (task 046): the named predicates the dashboard counts and links to, so a widget's
/// number and the list it opens are the SAME query (CaseListQueryBuilder.Filtered). Each is index-friendly.
/// </summary>
public enum CaseListSpecial
{
    /// <summary>ACTIVE, no data entered, cases.assigned_at ≤ now − 30 days (ix_cases_reminder).</summary>
    MissingData30,

    /// <summary>The same after 6 months.</summary>
    MissingData6M,

    /// <summary>The next hearing (the earliest SCHEDULED one, past ones included) is in the past (ix_cases_next_hearing).</summary>
    HearingOverdue,

    /// <summary>A SCHEDULED hearing this Tirane week, Monday 00:00 to the next Monday (ix_ch_case_time).</summary>
    HearingThisWeek,

    /// <summary>CLOSED, the reasoned decision not received yet (case_decisions.decision_received = 0). Default status CLOSED.</summary>
    DecisionPending,

    /// <summary>No current jurist of the caller's institution on the case (institution users).</summary>
    NoJurist,

    /// <summary>Delegated to the caller's institution (an active DELEGATED link).</summary>
    Delegated,
}

/// <summary>
/// A special filter with the values it needs, fixed when the request is parsed (one "now" for the whole request):
/// a cutoff, a week range, the caller's institution and its jurists.
/// </summary>
public sealed record CaseListSpecialFilter(
    CaseListSpecial Kind,
    DateTime? CutoffUtc = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    uint? InstitutionId = null,
    IReadOnlyList<uint>? JuristUserIds = null)
{
    public static string Code(CaseListSpecial kind) => kind switch
    {
        CaseListSpecial.MissingData30 => "missingData30",
        CaseListSpecial.MissingData6M => "missingData6m",
        CaseListSpecial.HearingOverdue => "hearingOverdue",
        CaseListSpecial.HearingThisWeek => "hearingThisWeek",
        CaseListSpecial.DecisionPending => "decisionPending",
        CaseListSpecial.NoJurist => "noJurist",
        CaseListSpecial.Delegated => "delegated",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool TryParse(string? code, out CaseListSpecial kind)
    {
        foreach (var candidate in Enum.GetValues<CaseListSpecial>())
        {
            if (string.Equals(Code(candidate), code?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    /// <summary>Whether the filter needs the caller's institution (institution users only).</summary>
    public static bool NeedsInstitution(CaseListSpecial kind) => kind is CaseListSpecial.NoJurist or CaseListSpecial.Delegated;

    /// <summary>Monday 00:00 Tirane of the week of <paramref name="nowUtc"/> and the next Monday, in UTC.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) TiraneWeek(DateTime nowUtc)
    {
        var today = TiraneTime.DateOf(nowUtc);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return (TiraneTime.StartOfDayUtc(monday), TiraneTime.StartOfDayUtc(monday.AddDays(7)));
    }

    /// <summary>The filter for <paramref name="kind"/> at <paramref name="nowUtc"/> (institution values given by the caller).</summary>
    public static CaseListSpecialFilter Create(
        CaseListSpecial kind, DateTime nowUtc, uint? institutionId = null, IReadOnlyList<uint>? juristUserIds = null) => kind switch
    {
        CaseListSpecial.MissingData30 => new(kind, CutoffUtc: nowUtc.AddDays(-30)),
        CaseListSpecial.MissingData6M => new(kind, CutoffUtc: nowUtc.AddMonths(-6)),
        CaseListSpecial.HearingOverdue => new(kind, CutoffUtc: nowUtc),
        CaseListSpecial.HearingThisWeek => new(kind, FromUtc: TiraneWeek(nowUtc).FromUtc, ToUtc: TiraneWeek(nowUtc).ToUtc),
        CaseListSpecial.NoJurist => new(kind, InstitutionId: institutionId, JuristUserIds: juristUserIds ?? []),
        CaseListSpecial.Delegated => new(kind, InstitutionId: institutionId),
        _ => new(kind),
    };
}
