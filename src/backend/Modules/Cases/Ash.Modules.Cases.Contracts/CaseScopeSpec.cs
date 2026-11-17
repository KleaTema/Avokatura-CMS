namespace Ash.Modules.Cases.Contracts;

/// <summary>
/// WHICH cases the current user may see, as data (task 042): the one decision of the access rules (which branches of
/// v_case_access apply to this user), which the case list (CaseScopeQuery, EF) and the reports (SQL) both render — so
/// the rules exist once. A case is in scope when ANY set branch matches:
/// <list type="bullet">
/// <item><see cref="All"/>: every case (case.view_all — Monitor, Admin).</item>
/// <item><see cref="OfficeId"/>: the cases of that office (case.view_office — Kryetar).</item>
/// <item>always: a current PRIMARY_LAWYER / SECONDARY_LAWYER / LEGAL_ASSISTANT assignment of <see cref="UserId"/>.</item>
/// <item><see cref="LinkedInstitutionId"/>: an active link of that institution (INSTITUTION_USER, INSTITUTION_DELEGATE).</item>
/// <item><see cref="JuristInstitutionId"/>: a current INSTITUTION_JURIST assignment on a case MONITORED by that institution.</item>
/// <item>always: a REGISTERED case <see cref="UserId"/> registered (Protokoll, until assigned).</item>
/// <item><see cref="SupervisorUserId"/>: the cases under that Kryetar's active supervision (task 053d,
/// case.view_supervised) — a supervised CASE, or a case whose CURRENT primary is a supervised LAWYER.</item>
/// </list>
/// </summary>
public sealed record CaseScopeSpec(
    bool All, long UserId, long? OfficeId, long? LinkedInstitutionId, long? JuristInstitutionId, long? SupervisorUserId = null)
{
    /// <summary>
    /// The supervision branch as SQL (task 053d), for the modules that query with SQL (reports, statistics): true when
    /// the case <paramref name="caseIdSql"/> is under the active supervision of the Kryetar <paramref name="headParam"/>.
    /// The EF rendering is CaseScopeQuery.Supervised — keep them in step (SupervisionTests compare every surface).
    /// </summary>
    public static string SupervisedSql(string caseIdSql, string headParam) => $"""
        EXISTS (SELECT 1 FROM supervisions sv
                 WHERE sv.head_user_id = {headParam} AND sv.ended_at IS NULL
                   AND ((sv.kind = 'CASE' AND sv.case_id = {caseIdSql})
                        OR (sv.kind = 'LAWYER' AND EXISTS (SELECT 1 FROM case_assignments sa
                                                            WHERE sa.case_id = {caseIdSql} AND sa.user_id = sv.lawyer_user_id
                                                              AND sa.case_role = '{PrimaryLawyer}' AND sa.ended_at IS NULL))))
        """;

    public const string PrimaryLawyer = "PRIMARY_LAWYER";
    public const string SecondaryLawyer = "SECONDARY_LAWYER";
    public const string LegalAssistant = "LEGAL_ASSISTANT";
    public const string InstitutionJurist = "INSTITUTION_JURIST";
    /// <summary>"Institucioni" (task 053k: monitoring, assigns the jurist).</summary>
    public const string InstitutionUser = "INSTITUTION_USER";
    /// <summary>"Institucioni me delegim" (task 053k): acts on the cases delegated to its institution.</summary>
    public const string InstitutionDelegate = "INSTITUTION_DELEGATE";
    public const string Registered = "REGISTERED";

    /// <summary>The lawyer roles of v_case_access's first branch.</summary>
    public static readonly IReadOnlyList<string> LawyerRoles = [PrimaryLawyer, SecondaryLawyer, LegalAssistant];
}

public interface ICaseScope
{
    /// <summary>The current user's scope (see <see cref="CaseScopeSpec"/>).</summary>
    CaseScopeSpec Current();
}

/// <summary>
/// The shared case search (task 047) as SQL for modules that query with SQL (the reports): <see cref="Sql"/> selects
/// the matching case ids as a column named Value and uses ONE parameter, @search_words = <see cref="Words"/>.
/// Unscoped: the caller combines it with its own scope.
/// </summary>
public sealed record CaseSearchSql(string Sql, string Words);

public interface ICaseSearchSql
{
    /// <summary>Null when the text has no searchable word (then nothing matches).</summary>
    Task<CaseSearchSql?> ResolveAsync(string q, CancellationToken ct = default);
}
