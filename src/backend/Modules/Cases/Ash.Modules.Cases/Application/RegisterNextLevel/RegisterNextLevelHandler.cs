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
/// Continues a closed case at the next level: X/n stays unchanged; X/n+1 is created in the same dossier with the
/// copied object text, parties and active institution links, the first hearing, and goes to the Kryetar of the
/// Protokoll's office. Assignments, category and decision are NOT copied. All in the request transaction.
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
        if (errors.Any)
        {
            return errors.ToError(CaseRegistrationSupport.InvalidMessage);
        }

        support.WarnIfHearingInPast(hearingUtc, warnings);

        var office = await support.CheckOfficeAsync(ct);
        if (office.IsFailure)
        {
            return office.Error!;
        }

        var (officeId, head) = office.Value;
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

        // 4. The non-deleted parties, then the active institution links (same mode, unless the new level does not
        //    allow delegation)
        var roles = (await reference.ListPartyRolesAsync(ct)).ToDictionary(r => r.Id);
        var sourceParties = await db.CaseParties.AsNoTracking()
            .Where(p => p.CaseId == previous.Id && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
        foreach (var party in sourceParties)
        {
            await parties.CopyAsync(next, party, roles[party.PartyRoleId], ct);
        }

        await CopyLinksAsync(previous, next, lookups.CourtLevel, warnings, ct);

        history.Add((long)dossier.Id, caseId, HistoryEventType.SentToHead,
            $"Çështja iu dërgua Kryetarit të zyrës: {head.FullName}.", @new: new { Name = head.FullName, UserPublicId = head.PublicId });
        await db.SaveChangesAsync(ct);

        audit.Record(AuditActions.CaseCreate, "CASE", caseId, caseId,
            details: new { next.CaseCode, PreviousCaseCode = previous.CaseCode });
        await events.PublishAsync(new CaseRegistered(next.PublicId, officeId, head.PublicId), ct);

        return new RegisterCaseResponse(next.PublicId, next.CaseCode, warnings);
    }

    private async Task CopyLinksAsync(
        Case previous, Case next, CourtLevelDto newLevel, List<RegistrationWarning> warnings, CancellationToken ct)
    {
        var links = await db.CaseInstitutions.AsNoTracking()
            .Where(l => l.CaseId == previous.Id && l.EndedAt == null)
            .OrderBy(l => l.Id)
            .ToListAsync(ct);
        if (links.Count == 0)
        {
            return;
        }

        var known = (await institutions.GetByIdsAsync(links.Select(l => (long)l.InstitutionId).ToList(), ct))
            .ToDictionary(i => i.Id);
        foreach (var link in links)
        {
            var institution = known[link.InstitutionId];
            var mode = link.LinkMode;
            if (mode == CaseInstitution.Delegated && !newLevel.AllowsDelegation)
            {
                mode = CaseInstitution.Monitoring;
                warnings.Add(new RegistrationWarning("institution.delegation_not_allowed",
                    $"Institucioni {institution.Name} kishte delegim; në shkallën {newLevel.Name} delegimi nuk lejohet, " +
                    "prandaj u lidh si monitorues."));
            }

            await parties.LinkAsync(next, institution, mode, ct);
        }
    }

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
