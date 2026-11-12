namespace Ash.Modules.Cases.Contracts;

/// <summary>
/// WHICH cases the current user may see, as data (task 042): the one decision of the access rules (which branches of
/// v_case_access apply to this user), which the case list (CaseScopeQuery, EF) and the reports (SQL) both render — so
/// the rules exist once. A case is in scope when ANY set branch matches:
/// <list type="bullet">
/// <item><see cref="All"/>: every case (case.view_all — Monitor, Admin).</item>
/// <item><see cref="OfficeId"/>: the cases of that office (case.view_office — Kryetar).</item>
/// <item>always: a current PRIMARY_LAWYER / SECONDARY_LAWYER / LEGAL_ASSISTANT assignment of <see cref="UserId"/>.</item>
/// <item><see cref="LinkedInstitutionId"/>: an active link of that institution (INSTITUTION_USER).</item>
/// <item><see cref="JuristInstitutionId"/>: a current INSTITUTION_JURIST assignment on a case linked to that institution.</item>
/// <item>always: a REGISTERED case <see cref="UserId"/> registered (Protokoll, until assigned).</item>
/// </list>
/// </summary>
public sealed record CaseScopeSpec(bool All, long UserId, long? OfficeId, long? LinkedInstitutionId, long? JuristInstitutionId)
{
    public const string PrimaryLawyer = "PRIMARY_LAWYER";
    public const string SecondaryLawyer = "SECONDARY_LAWYER";
    public const string LegalAssistant = "LEGAL_ASSISTANT";
    public const string InstitutionJurist = "INSTITUTION_JURIST";
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
