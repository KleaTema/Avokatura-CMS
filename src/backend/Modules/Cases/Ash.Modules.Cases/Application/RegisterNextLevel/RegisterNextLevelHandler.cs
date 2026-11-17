using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.Parties;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.RegisterNextLevel;

/// <summary>
/// Continues a closed case at the next level: X/n stays unchanged; X/n+1 is created in the same dossier with ONLY the
/// copied object text and the parties of a copied role (plaintiff, defendant — task 053j c), a MONITORING link for each
/// institution among those, the parties the Protokoll added, the first hearing, and goes to the Kryetar of the office
/// that covers the summons court (or the office chosen — task 053j d). Assignments, category, subcategory, delegation
/// and decision are NOT copied (client decision B.1, 12.11.2026). Any court level may follow, a lower one too (a
/// retrial). All in the request transaction.
/// </summary>
internal sealed class RegisterNextLevelHandler(
    CasesDbContext db,
    NextLevelGate gate,
    CaseRegistrationSupport support,
    IReferenceData reference,
    IInstitutionDirectory institutions,
    PartyWriter parties,
    IHistoryWriter history,
    IAuditLog audit,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public async Task<Result<RegisterCaseResponse>> Handle(Guid previousCasePublicId, RegisterNextLevelRequest req, CancellationToken ct)
    {
        // 1. The previous level must be the latest, its dossier open, and the level closed in the system.
        //    The previous case is only read, never changed (no tracking).
        var source = await gate.CheckAsync(previousCasePublicId, ct);
        if (source.IsFailure)
        {
            return source.Error!;
        }

        var (previous, dossier, latestLevelSeq) = source.Value;

        // 2. The form, as in task 016 (object text and parties come from the previous level)
        var form = req.AsRegistration(previous.ObjectText);
        var errors = new FieldErrors();
        var hearingUtc = RegisterCaseValidator.Validate(form, TiraneTime.Today(clock), errors, withParties: false);
        var warnings = new List<RegistrationWarning>();
        var lookups = await support.ResolveLookupsAsync(form, errors, warnings, ct);
        var addedParties = await support.ResolvePartiesAsync(form.Parties, errors, ct);
        if (errors.Any)
        {
            return errors.ToError(CaseRegistrationSupport.InvalidMessage);
        }

        support.WarnIfHearingInPast(hearingUtc, warnings);

        // "Zyra që e ndjek" (task 053j): the office of the court that issued the new summons, or the one chosen
        var office = await support.ResolveOfficeAsync(lookups.Court.Id, req.OfficePublicId, ct);
        if (office.IsFailure)
        {
            return office.Error!;
        }

        var officeId = office.Value.Office.Id;
        var head = office.Value.Head;
        var summonsNumber = form.SummonsNumber!.Trim();
        var actNumber = form.ActNumber!.Trim();
        var duplicates = await support.CheckPossibleDuplicatesAsync(
            (ushort)officeId, summonsNumber, lookups.Court.Id, actNumber, req.ConfirmPossibleDuplicate, dossier.Id, ct);
        if (duplicates.Conflict is not null)
        {
            return duplicates.Conflict;
        }

        // 3. The new level; a parallel request continuing the same case hits uq_cases_dossier_level → 409
        var levelSeq = (ushort)(latestLevelSeq + 1);
        var next = new Case
        {
            PublicId = Guid.NewGuid(),
            DossierId = dossier.Id,
            LevelSeq = levelSeq,
            CaseCode = $"{dossier.UniqueCode}/{levelSeq}",
            PreviousCaseId = previous.Id,
            OfficeId = (ushort)officeId,
            SummonsNumber = summonsNumber,
            SummonsTypeId = lookups.SummonsType?.Id,
            ActNumber = actNumber,
            ActDate = form.ActDate,
            ReceivedDate = form.ReceivedDate!.Value,
            CourtLevelId = lookups.CourtLevel.Id,
            CourtId = lookups.Court.Id,
            JudgeId = lookups.Judge.Id,
            ObjectText = previous.ObjectText,
            LifecycleStatus = Case.Registered,
            NextHearingAt = hearingUtc,
            RegisteredBy = (uint)user.UserId,
            RegisteredAt = clock.UtcNow
        };
        db.Cases.Add(next);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException mysql
                                           && NextLevelInsertConflict.Map(mysql.Number, mysql.Message, next.CaseCode) is { } conflict)
        {
            return conflict;
        }

        support.AddFirstHearing(next, lookups, form.FirstHearing!, hearingUtc!.Value);
        var caseId = (long)next.Id;
        history.Add((long)dossier.Id, caseId, HistoryEventType.CaseCreatedFromPrevious,
            $"Çështja {next.CaseCode} u krijua nga {previous.CaseCode} (fletëthirrja nr. {summonsNumber}).",
            @new: new
            {
                next.CaseCode,
                PreviousCaseCode = previous.CaseCode,
                SummonsNumber = summonsNumber,
                ActNumber = actNumber,
                Court = lookups.Court.Name,
                Judge = $"{lookups.Judge.FirstName} {lookups.Judge.LastName}",
                PossibleDuplicateConfirmed = duplicates.Total > 0
            });
        await db.SaveChangesAsync(ct);

        // 4. The parties whose role is copied (party_roles.copy_to_next_level: plaintiff and defendant, task 053j),
        //    then a MONITORING link per institution among THEM (client decision B.1: no delegation, no category — the
        //    new level gets a new lawyer, who sets them); then the parties the Protokoll added in the form
        var roles = (await reference.ListPartyRolesAsync(ct)).ToDictionary(r => r.Id);
        var sourceParties = await CopiedPartiesAsync(db, previous.Id, roles, ct);
        foreach (var party in sourceParties)
        {
            await parties.CopyAsync(next, party, roles[party.PartyRoleId], ct);
        }

        await LinkInstitutionPartiesAsync(next, sourceParties, ct);
        foreach (var party in addedParties)
        {
            await parties.AddAsync(next, party, ct);
        }

        history.Add((long)dossier.Id, caseId, HistoryEventType.SentToHead,
            $"Çështja iu dërgua Kryetarit të zyrës: {head.FullName}.",
            @new: new { Name = head.FullName, UserPublicId = head.PublicId, Office = office.Value.Office.Name });
        await db.SaveChangesAsync(ct);

        audit.Record(AuditActions.CaseCreate, "CASE", caseId, caseId,
            details: new { next.CaseCode, PreviousCaseCode = previous.CaseCode });
        if (CaseRegistrationSupport.OverrideDetails(office.Value, lookups.Court) is { } overridden)
        {
            audit.Record(AuditActions.CaseOfficeOverride, "CASE", caseId, caseId, details: overridden);
        }
        await events.PublishAsync(new CaseRegistered(next.PublicId, officeId, head.PublicId), ct);

        return new RegisterCaseResponse(next.PublicId, next.CaseCode, warnings);
    }

    /// <summary>One MONITORING link per distinct institution among the copied parties, in party order.</summary>
    private async Task LinkInstitutionPartiesAsync(Case next, IReadOnlyList<CaseParty> copied, CancellationToken ct)
    {
        var ids = InstitutionIds(copied);
        if (ids.Count == 0)
        {
            return;
        }

        var known = (await institutions.GetByIdsAsync(ids, ct)).ToDictionary(i => i.Id);
        foreach (var id in ids)
        {
            await parties.LinkAsync(next, known[id], CaseInstitution.Monitoring, ct);
        }
    }

    /// <summary>
    /// The parties of the previous level that continuing copies (shared with the preview): not deleted, and of a role
    /// with copy_to_next_level (task 053j) — a third party, for one, is not copied.
    /// </summary>
    internal static async Task<List<CaseParty>> CopiedPartiesAsync(
        CasesDbContext db, ulong previousCaseId, IReadOnlyDictionary<byte, PartyRoleDto> roles, CancellationToken ct)
    {
        var all = await db.CaseParties.AsNoTracking()
            .Where(p => p.CaseId == previousCaseId && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
        return all.Where(p => roles.TryGetValue(p.PartyRoleId, out var role) && role.CopyToNextLevel).ToList();
    }

    /// <summary>The institutions of the parties, first occurrence first (shared with the preview).</summary>
    internal static List<long> InstitutionIds(IEnumerable<CaseParty> parties) =>
        parties.Where(p => p.InstitutionId is not null).Select(p => (long)p.InstitutionId!.Value).Distinct().ToList();

    internal static Error AlreadyContinued(string caseCode) => NextLevelGate.AlreadyContinued(caseCode);
}

/// <summary>
/// Maps a failed insert of the next level to the "already continued" 409 (never a 500): a parallel request that
/// committed the same level first gives 1062 on uq_cases_dossier_level (or uq_cases_code); a deadlock between the
/// two gives 1213. Any other database error is not mapped (null) and stays an error.
/// </summary>
public static class NextLevelInsertConflict
{
    public const int DuplicateKey = 1062;
    public const int Deadlock = 1213;

    public static Error? Map(int mySqlErrorNumber, string message, string attemptedCaseCode) =>
        mySqlErrorNumber switch
        {
            DuplicateKey when message.Contains("uq_cases_dossier_level", StringComparison.Ordinal)
                              || message.Contains("uq_cases_code", StringComparison.Ordinal)
                => RegisterNextLevelHandler.AlreadyContinued(attemptedCaseCode),
            Deadlock => RegisterNextLevelHandler.AlreadyContinued(attemptedCaseCode),
            _ => null
        };
}
