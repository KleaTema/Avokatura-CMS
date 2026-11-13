using System.Text;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Dapper;

namespace Ash.Modules.Reports.Application;

/// <summary>
/// Who may run a report, and on which cases (tasks 042/043). The cases are the user's case scope as Cases decides it
/// (<see cref="ICaseScope"/> → <see cref="CaseScopeSpec"/>, the same spec the case list renders) — written here as SQL
/// over the base tables, branch for branch: lawyer / assistant / jurist → a current assignment; Kryetar → the office;
/// institution user → the linked cases; Monitor / Admin → every case. Without a report.* permission (Protokoll) → 403.
/// TODO(client-q6): exact report access per role (default: the case list's scope).
/// </summary>
public sealed class ReportScope(ICaseScope scope, ICurrentUser user)
{
    public static readonly string[] ReportPermissions = ["report.view_own", "report.view_office", "report.view_all"];

    public Error? Denied() =>
        ReportPermissions.Any(user.HasPermission)
            ? null
            : Error.Forbidden("report.forbidden", "Nuk keni të drejtë të shihni raportet.");

    /// <summary>"(…)" over the alias c (cases), adding its parameters (@scope_*) to <paramref name="p"/>.</summary>
    public string Sql(DynamicParameters p) => Sql(scope.Current(), p);

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
            // branch 3: a current jurist assignment on a case linked to the jurist's institution
            p.Add("scope_jurist", jurist);
            sql.Append($"""
                 OR EXISTS (SELECT 1 FROM case_assignments a WHERE a.case_id = c.id AND a.user_id = @scope_user AND a.ended_at IS NULL
                             AND a.case_role = '{CaseScopeSpec.InstitutionJurist}'
                             AND EXISTS (SELECT 1 FROM case_institutions i WHERE i.case_id = c.id AND i.institution_id = @scope_jurist AND i.ended_at IS NULL))
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
