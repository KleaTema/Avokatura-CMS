using System.Text;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Reports.Infrastructure;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Dapper;

namespace Ash.Modules.Reports.Application;

/// <summary>What kind of reader a user is for the reports (Tabela 1/3) and the statistics (Tabela 5/6).</summary>
public enum ReportAudienceKind
{
    /// <summary>Monitor, Admin (report.view_all): every case.</summary>
    All,

    /// <summary>Kryetar (report.view_office): own office + supervised + own assignments (053d).</summary>
    Head,

    /// <summary>Institution user / jurist: every case of the institution (a ministry: its direct agencies too).</summary>
    Institution,

    /// <summary>"Avokat Shteti (primar)" and / or legal assistant: the lawyers of <see cref="ReportAudience.LawyerIds"/>.</summary>
    Lawyers,

    /// <summary>"Avokat sekondar" only: reports for the cases where a current member; no statistics (403).</summary>
    SecondaryOnly,
}

/// <summary>
/// Task 053h (decisions F, P6/P9): who sees which cases in the reports and statistics — decided HERE ONLY, for both.
/// <list type="bullet">
/// <item>Monitor / Admin → all.</item>
/// <item>Kryetar → own + office + supervised (the case list's scope, <see cref="CaseScopeSpec"/>, task 053d).</item>
/// <item>Institution user and jurist → every case where the institution is a party or linked, whatever the lawyer;
/// a ministry includes its direct agencies (one level, as Tabela 6 by ministry). TODO(client-q-ministry-scope).</item>
/// <item>STATE_LAWYER → himself. Statistics count a case for him as PRIMARY at the measure's time. Reports: the cases
/// where the user is a current member or the current primary.</item>
/// <item>SECONDARY_LAWYER or LEGAL_ASSISTANT without STATE_LAWYER → reports for the cases where a current member;
/// statistics 403 (053i). Task 053q: an assistant has no lawyers any more (the primary lawyer adds them per case), so
/// the assistant's reports are the cases they were added to.</item>
/// </list>
/// </summary>
public sealed record ReportAudience(
    ReportAudienceKind Kind,
    long UserId,
    long? OfficeId,
    long? InstitutionId,
    bool InstitutionWithChildren,
    IReadOnlyList<long> LawyerIds,
    CaseScopeSpec? HeadScope)
{
    public const string StateLawyerRole = "STATE_LAWYER";
    public const string SecondaryLawyerRole = "SECONDARY_LAWYER";
    public const string AssistantRole = "LEGAL_ASSISTANT";
    public const string InstitutionUserRole = "INSTITUTION_USER";
    public const string InstitutionJuristRole = "INSTITUTION_JURIST";
    public const string InstitutionDelegateRole = CaseScopeSpec.InstitutionDelegate;

    public static readonly Error ReportsForbidden = Error.Forbidden("report.forbidden", "Nuk keni të drejtë të shihni raportet.");
    public static readonly Error StatisticsForbidden =
        Error.Forbidden("statistics.forbidden", "Nuk keni të drejtë të shihni këto statistika.");

    /// <summary>The statistics are not for this reader (secondary-only).</summary>
    public bool StatisticsDenied => Kind == ReportAudienceKind.SecondaryOnly;

    /// <summary>
    /// "(…)" over the alias c (cases) for Tabela 1/3, adding its parameters (@aud_*) to <paramref name="p"/>.
    /// </summary>
    public string CasesSql(DynamicParameters p)
    {
        switch (Kind)
        {
            case ReportAudienceKind.All:
                return "TRUE";
            case ReportAudienceKind.Head:
                return ReportScope.Sql(HeadScope!, p);
            case ReportAudienceKind.Institution:
                p.Add("aud_inst", InstitutionId);
                p.Add("aud_inst_children", InstitutionWithChildren ? 1 : 0);
                return InstitutionSql("c.id", "@aud_inst", "@aud_inst_children");
        }

        // Lawyers / SecondaryOnly: a current member (lawyer or assistant row) …
        p.Add("aud_user", UserId);
        var sql = new StringBuilder($"""
            (EXISTS (SELECT 1 FROM case_assignments am WHERE am.case_id = c.id AND am.user_id = @aud_user AND am.ended_at IS NULL
                     AND am.case_role IN ({string.Join(", ", CaseScopeSpec.LawyerRoles.Select(r => $"'{r}'"))}))
            """);
        if (Kind == ReportAudienceKind.Lawyers && LawyerIds.Count > 0)
        {
            // … or one of the user's lawyers (himself, his linked lawyers) is the current primary
            p.Add("aud_lawyers", LawyerIds);
            sql.Append("""
                 OR EXISTS (SELECT 1 FROM case_assignments ap WHERE ap.case_id = c.id AND ap.case_role = 'PRIMARY_LAWYER'
                             AND ap.ended_at IS NULL AND ap.user_id IN @aud_lawyers)
                """);
        }

        return sql.Append(')').ToString();
    }

    /// <summary>
    /// The institution is a non-deleted party of the case or has an active link to it (with children: or one of its
    /// direct agencies) — the same rule as Tabela 6 (StatisticsFlags.sql).
    /// </summary>
    public static string InstitutionSql(string caseId, string institution, string withChildren) => $"""
        (EXISTS (SELECT 1 FROM case_parties ip JOIN institutions ii ON ii.id = ip.institution_id
                  WHERE ip.case_id = {caseId} AND ip.deleted_at IS NULL
                    AND (ii.id = {institution} OR ({withChildren} = 1 AND ii.parent_institution_id = {institution})))
         OR EXISTS (SELECT 1 FROM case_institutions il JOIN institutions ij ON ij.id = il.institution_id
                     WHERE il.case_id = {caseId} AND il.ended_at IS NULL
                       AND (ij.id = {institution} OR ({withChildren} = 1 AND ij.parent_institution_id = {institution}))))
        """;
}

/// <summary>Builds the <see cref="ReportAudience"/> of the current user (once per request).</summary>
public sealed class ReportAudienceResolver(ICurrentUser user, ICaseScope caseScope, ReportingDb db)
{
    private ReportAudience? _resolved;

    public async Task<Result<ReportAudience>> ResolveAsync(CancellationToken ct)
    {
        if (_resolved is not null)
        {
            return _resolved;
        }

        if (!ReportScope.ReportPermissions.Any(user.HasPermission))
        {
            return ReportAudience.ReportsForbidden;
        }

        _resolved = await BuildAsync(ct);
        return _resolved is null ? ReportAudience.ReportsForbidden : _resolved;
    }

    private async Task<ReportAudience?> BuildAsync(CancellationToken ct)
    {
        ReportAudience Make(ReportAudienceKind kind, IReadOnlyList<long>? lawyers = null, long? institution = null,
            bool children = false, CaseScopeSpec? head = null) =>
            new(kind, user.UserId, user.OfficeId, institution, children, lawyers ?? [], head);

        if (user.HasPermission("report.view_all"))
        {
            return Make(ReportAudienceKind.All);
        }

        if (user.HasPermission("report.view_office") && user.OfficeId is not null)
        {
            return Make(ReportAudienceKind.Head, head: caseScope.Current());
        }

        var roles = user.Roles;
        // decision P6 (053h, confirmed by 053k): every institution role sees EVERY case of its institution (party or
        // linked), whatever the lawyer — "Institucioni", its jurist and "Institucioni me delegim"
        if (user.InstitutionId is { } institution
            && (roles.Contains(ReportAudience.InstitutionUserRole) || roles.Contains(ReportAudience.InstitutionJuristRole)
                || roles.Contains(ReportAudience.InstitutionDelegateRole)))
        {
            await using var connection = await db.OpenAsync(ct);
            var type = await db.ScalarAsync<string?>(connection,
                "SELECT institution_type FROM institutions WHERE id = @id", new { id = institution }, false, ct);
            return Make(ReportAudienceKind.Institution, institution: institution, children: type == "MINISTRY");
        }

        if (user.OfficeId is null)
        {
            return null;
        }

        if (roles.Contains(ReportAudience.StateLawyerRole))
        {
            return Make(ReportAudienceKind.Lawyers, [user.UserId]);
        }

        // task 053q: an assistant, like a secondary-only lawyer, sees the cases where a current member
        return roles.Contains(ReportAudience.SecondaryLawyerRole) || roles.Contains(ReportAudience.AssistantRole)
            ? Make(ReportAudienceKind.SecondaryOnly)
            : null;
    }
}
