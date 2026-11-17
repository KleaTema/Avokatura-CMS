using System.Text;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Dapper;

namespace Ash.Modules.Reports.Application;

/// <summary>
/// Who may run a report (Tabela 1/3), and on which cases. Since task 053h the rules live in <see cref="ReportAudience"/>
/// (shared with the statistics); this class gives the reports their 403 and their WHERE. Without a report.*
/// permission (Protokoll) → 403. <see cref="Sql(CaseScopeSpec, DynamicParameters)"/> renders the Kryetar's case scope
/// (the case list's <see cref="CaseScopeSpec"/>, task 053d) as SQL over the base tables, branch for branch.
/// </summary>
public sealed class ReportScope(ReportAudienceResolver audience)
{
    public static readonly string[] ReportPermissions = ["report.view_own", "report.view_office", "report.view_all"];

    public async Task<Error?> DeniedAsync(CancellationToken ct)
    {
        var resolved = await audience.ResolveAsync(ct);
        return resolved.IsFailure ? resolved.Error : null;
    }

    /// <summary>"(…)" over the alias c (cases), adding its parameters to <paramref name="p"/>; call after DeniedAsync.</summary>
    public async Task<string> SqlAsync(DynamicParameters p, CancellationToken ct)
    {
        var resolved = await audience.ResolveAsync(ct);
        return resolved.IsSuccess ? resolved.Value.CasesSql(p) : "FALSE";
    }

    public static string Sql(CaseScopeSpec spec, DynamicParameters p)
    {
        if (spec.All)
        {
            return "TRUE";
        }

        p.Add("scope_user", spec.UserId);
        var sql = new StringBuilder("(");
        // a REGISTERED case the user registered (Protokoll — never here: no report permission; kept to match the list)
        sql.Append($"(c.registered_by = @scope_user AND c.lifecycle_status = '{CaseScopeSpec.Registered}')");
        // branch 1: a current lawyer / assistant assignment (ix_ca_case_current)
        sql.Append($"""
             OR EXISTS (SELECT 1 FROM case_assignments a WHERE a.case_id = c.id AND a.user_id = @scope_user AND a.ended_at IS NULL
                         AND a.case_role IN ({string.Join(", ", CaseScopeSpec.LawyerRoles.Select(r => $"'{r}'"))}))
            """);
        if (spec.LinkedInstitutionId is { } linked)
        {
            // branch 2: an active link of the user's institution (ix_ci_case)
            p.Add("scope_linked", linked);
            sql.Append("""
                 OR EXISTS (SELECT 1 FROM case_institutions i WHERE i.case_id = c.id AND i.institution_id = @scope_linked AND i.ended_at IS NULL)
                """);
        }

        if (spec.JuristInstitutionId is { } jurist)
        {
            // branch 3: a current jurist assignment on a case MONITORED by the jurist's institution (task 053k)
            p.Add("scope_jurist", jurist);
            sql.Append($"""
                 OR EXISTS (SELECT 1 FROM case_assignments a WHERE a.case_id = c.id AND a.user_id = @scope_user AND a.ended_at IS NULL
                             AND a.case_role = '{CaseScopeSpec.InstitutionJurist}'
                             AND EXISTS (SELECT 1 FROM case_institutions i WHERE i.case_id = c.id AND i.institution_id = @scope_jurist
                                          AND i.ended_at IS NULL AND i.link_mode = 'MONITORING'))
                """);
        }

        if (spec.SupervisorUserId is { } head)
        {
            // branch 4 (task 053d): under this Kryetar's supervision
            p.Add("scope_head", head);
            sql.Append(" OR ").Append(CaseScopeSpec.SupervisedSql("c.id", "@scope_head"));
        }

        if (spec.OfficeId is { } office)
        {
            p.Add("scope_office", office);
            sql.Append(" OR c.office_id = @scope_office");
        }

        return sql.Append(')').ToString();
    }
}
