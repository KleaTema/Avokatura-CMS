using System.Data;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.Parties;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace Ash.Modules.Cases.Application.RegisterCase;

/// <summary>
/// Protokoll registers a summons: dossier with its unique code (create_dossier), case X/1 in REGISTERED,
/// the first hearing, the parties (with institution links), the history rows, and CaseRegistered for the
/// office head — all in the request transaction, so any failure leaves nothing behind.
/// </summary>
internal sealed class RegisterCaseHandler(
    CasesDbContext db,
    IUnitOfWork unitOfWork,
    CaseRegistrationSupport support,
    IHistoryWriter history,
    PartyResolver partyResolver,
    PartyWriter parties,
    IAuditLog audit,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public async Task<Result<RegisterCaseResponse>> Handle(RegisterCaseRequest req, CancellationToken ct)
    {
        // 1. Fields and dates
        var errors = new FieldErrors();
        var hearingUtc = RegisterCaseValidator.Validate(req, TiraneTime.Today(clock), errors);
        var warnings = new List<RegistrationWarning>();

        // 2. Lookups (only for the fields that are present)
        var lookups = await support.ResolveLookupsAsync(req, errors, warnings, ct);
        var newParties = await ResolvePartiesAsync(req.Parties, errors, ct);
        if (errors.Any)
        {
            return errors.ToError(CaseRegistrationSupport.InvalidMessage);
        }

        support.WarnIfHearingInPast(hearingUtc, warnings);

        // 3. Office and its head — before create_dossier so no unique code is spent on a refused request
        var office = await support.CheckOfficeAsync(ct);
        if (office.IsFailure)
        {
            return office.Error!;
        }

        var (officeId, head) = office.Value;

        // 4. Possible duplicate
        var summonsNumber = req.SummonsNumber!.Trim();
        var actNumber = req.ActNumber!.Trim();
        var courtId = lookups.Court.Id;
        var duplicates = await support.CheckPossibleDuplicatesAsync(
            (ushort)officeId, summonsNumber, courtId, actNumber, req.ConfirmPossibleDuplicate, null, ct);
        if (duplicates.Conflict is not null)
        {
            return duplicates.Conflict;
        }

        // 5. Dossier and its unique code
        var (dossierId, uniqueCode) = await CreateDossierAsync(officeId, user.UserId, ct);
        var dossier = await db.Dossiers.AsNoTracking().SingleAsync(d => d.Id == dossierId, ct);

        // 6. Case X/1 and its first hearing
        var registered = new Case
        {
            PublicId = Guid.NewGuid(),
            DossierId = dossier.Id,
            LevelSeq = 1,
            CaseCode = $"{uniqueCode}/1",
            OfficeId = (ushort)officeId,
            SummonsNumber = summonsNumber,
            SummonsTypeId = lookups.SummonsType?.Id,
            ActNumber = actNumber,
            ActDate = req.ActDate,
            ReceivedDate = req.ReceivedDate!.Value,
            CourtLevelId = lookups.CourtLevel.Id,
            CourtId = courtId,
            JudgeId = lookups.Judge.Id,
            ObjectText = req.ObjectText!.Trim(),
            LifecycleStatus = Case.Registered,
            NextHearingAt = hearingUtc,
            RegisteredBy = (uint)user.UserId,
            RegisteredAt = clock.UtcNow
        };
        db.Cases.Add(registered);
        await db.SaveChangesAsync(ct);
        support.AddFirstHearing(registered, lookups, req.FirstHearing!, hearingUtc!.Value);

        // 7. History, audit and the event — same transaction
        var caseId = (long)registered.Id;
        history.Add((long)dossier.Id, null, HistoryEventType.DossierCreated,
            $"Dosja {uniqueCode} u krijua.", @new: new { UniqueCode = uniqueCode });
        history.Add((long)dossier.Id, caseId, HistoryEventType.CaseRegistered,
            $"Çështja {registered.CaseCode} u regjistrua nga fletëthirrja nr. {summonsNumber}.",
            @new: new
            {
                registered.CaseCode,
                SummonsNumber = summonsNumber,
                ActNumber = actNumber,
                Court = lookups.Court.Name,
                Judge = $"{lookups.Judge.FirstName} {lookups.Judge.LastName}",
                PossibleDuplicateConfirmed = duplicates.Total > 0
            });
        await db.SaveChangesAsync(ct);

        // 7b. Parties: PARTY_ADDED each, institution links with INSTITUTION_LINKED and InstitutionLinked
        foreach (var party in newParties)
        {
            await parties.AddAsync(registered, party, ct);
        }

        history.Add((long)dossier.Id, caseId, HistoryEventType.SentToHead,
            $"Çështja iu dërgua Kryetarit të zyrës: {head.FullName}.", @new: new { Name = head.FullName, UserPublicId = head.PublicId });
        await db.SaveChangesAsync(ct);

        audit.Record(AuditActions.CaseCreate, "CASE", caseId, caseId, details: new { registered.CaseCode });
        await events.PublishAsync(new CaseRegistered(registered.PublicId, officeId, head.PublicId), ct);

        return new RegisterCaseResponse(registered.PublicId, registered.CaseCode, warnings);
    }

    /// <summary>Roles and institutions must exist and be active; returns the parties ready for <see cref="PartyWriter"/>.</summary>
    private async Task<List<NewParty>> ResolvePartiesAsync(
        IReadOnlyList<PartyRequest>? requested, FieldErrors errors, CancellationToken ct)
    {
        var result = new List<NewParty>();
        for (var i = 0; i < (requested?.Count ?? 0); i++)
        {
            var p = requested![i];
            var prefix = $"parties[{i}].";
            var kind = p?.Kind?.Trim();
            if (p is null || kind is null || errors.Has($"{prefix}kind"))
            {
                continue; // reported by the validator
            }

            if (await partyResolver.ResolveAsync(p, kind, prefix, errors, ct) is { } party)
            {
                result.Add(party);
            }
        }

        return result;
    }

    /// <summary>create_dossier issues the unique code and inserts the dossier, on the request's connection and transaction.</summary>
    private async Task<(ulong DossierId, string UniqueCode)> CreateDossierAsync(long officeId, long userId, CancellationToken ct)
    {
        await using var command = unitOfWork.CreateCommand("create_dossier");
        command.CommandType = CommandType.StoredProcedure;
        command.Parameters.Add(new MySqlParameter("p_office_id", MySqlDbType.UInt16) { Value = officeId });
        command.Parameters.Add(new MySqlParameter("p_user_id", MySqlDbType.UInt32) { Value = userId });
        var idParam = new MySqlParameter("p_dossier_id", MySqlDbType.UInt64) { Direction = ParameterDirection.Output };
        var codeParam = new MySqlParameter("p_code", MySqlDbType.VarChar, 30) { Direction = ParameterDirection.Output };
        command.Parameters.Add(idParam);
        command.Parameters.Add(codeParam);
        await command.ExecuteNonQueryAsync(ct);
        return (Convert.ToUInt64(idParam.Value), (string)codeParam.Value);
    }
}
