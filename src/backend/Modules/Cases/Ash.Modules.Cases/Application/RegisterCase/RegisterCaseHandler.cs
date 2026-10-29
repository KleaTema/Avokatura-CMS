using System.Data;
using Ash.SharedKernel.Events;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.Parties;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
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
    IReferenceData reference,
    IOfficeDirectory offices,
    ICaseAccessService access,
    IHistoryWriter history,
    PartyResolver partyResolver,
    PartyWriter parties,
    IAuditLog audit,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public const string DefaultHearingTypeCode = "PERGATITORE";

    public async Task<Result<RegisterCaseResponse>> Handle(RegisterCaseRequest req, CancellationToken ct)
    {
        // 1. Fields and dates
        var errors = new FieldErrors();
        var hearingUtc = RegisterCaseValidator.Validate(req, TiraneTime.Today(clock), errors);
        var warnings = new List<RegistrationWarning>();

        // 2. Lookups (only for the fields that are present)
        var lookups = await ResolveLookupsAsync(req, errors, warnings, ct);
        var newParties = await ResolvePartiesAsync(req.Parties, errors, ct);
        if (errors.Any)
        {
            return Error.ValidationFields("Të dhënat e regjistrimit nuk janë të vlefshme.", errors.ToDictionary());
        }

        if (hearingUtc < clock.UtcNow)
        {
            warnings.Add(new RegistrationWarning("hearing.in_past",
                "Data e seancës së parë është në të kaluarën. Kontrolloni nëse është regjistrim i vonuar."));
        }

        // 3. Office and its head — checked before create_dossier so no unique code is spent on a refused request.
        // TODO(client-q-office-without-head): registration is refused while the office has no active head (default for task 016).
        if (user.OfficeId is not { } officeId)
        {
            return Error.Validation("case.no_office", "Përdoruesi nuk i përket asnjë zyre.");
        }

        var routing = await offices.GetRoutingAsync(officeId, ct);
        if (routing is null || !routing.IsActive)
        {
            return Error.Validation("case.office_inactive", "Zyra nuk është aktive. Kontaktoni administratorin.");
        }

        if (routing.Head is not { IsActive: true } head)
        {
            return Error.Validation("case.office_without_head", "Zyra nuk ka Kryetar. Kontaktoni administratorin.");
        }

        // 4. Possible duplicate
        var summonsNumber = req.SummonsNumber!.Trim();
        var actNumber = req.ActNumber!.Trim();
        var courtId = lookups.Court.Id;
        var duplicates = await FindPossibleDuplicatesAsync((ushort)officeId, summonsNumber, courtId, actNumber, ct);
        if (duplicates.Total > 0 && !req.ConfirmPossibleDuplicate)
        {
            // TODO(client-q1): the same act number usually means the next level of an existing case (task 018).
            return Error.Conflict("case.possible_duplicate",
                    "Ekziston tashmë një çështje me të njëjtin numër fletëthirrjeje dhe gjykatë, ose me të njëjtin numër akti. Konfirmoni nëse doni ta regjistroni gjithsesi.")
                with
                {
                    Extensions = new Dictionary<string, object?>
                    {
                        ["matches"] = duplicates.Visible,
                        ["hiddenCount"] = duplicates.Total - duplicates.Visible.Count
                    }
                };
        }

        // 5. Dossier and its unique code
        var (dossierId, uniqueCode) = await CreateDossierAsync(officeId, user.UserId, ct);
        var dossier = await db.Dossiers.AsNoTracking().SingleAsync(d => d.Id == dossierId, ct);

        // 6. Case X/1 and its first hearing
        var now = clock.UtcNow;
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
            RegisteredAt = now
        };
        db.Cases.Add(registered);
        await db.SaveChangesAsync(ct);

        db.CaseHearings.Add(new CaseHearing
        {
            CaseId = registered.Id,
            HearingTypeId = lookups.HearingType.Id,
            OtherTypeText = lookups.HearingType.IsOther ? req.FirstHearing!.OtherTypeText!.Trim() : null,
            ScheduledAt = hearingUtc!.Value,
            Status = CaseHearing.Scheduled,
            CreatedBy = (uint)user.UserId
        });

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
                CourtId = courtId,
                JudgeId = lookups.Judge.Id,
                PossibleDuplicateConfirmed = duplicates.Total > 0
            });
        await db.SaveChangesAsync(ct);

        // 7b. Parties: PARTY_ADDED each, institution links with INSTITUTION_LINKED and InstitutionLinked
        foreach (var party in newParties)
        {
            await parties.AddAsync(registered, party, ct);
        }

        history.Add((long)dossier.Id, caseId, HistoryEventType.SentToHead,
            $"Çështja iu dërgua Kryetarit të zyrës: {head.FullName}.", @new: new { HeadUserId = head.UserId });
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

    private sealed record Lookups(
        CourtLevelDto CourtLevel, CourtDto Court, JudgeDto Judge, SummonsTypeDto? SummonsType, HearingTypeDto HearingType);

    private async Task<Lookups> ResolveLookupsAsync(
        RegisterCaseRequest req, FieldErrors errors, List<RegistrationWarning> warnings, CancellationToken ct)
    {
        CourtLevelDto? level = null;
        if (!string.IsNullOrWhiteSpace(req.CourtLevelCode))
        {
            level = await reference.GetCourtLevelByCode(req.CourtLevelCode.Trim(), ct);
            if (level is null)
            {
                errors.Add("courtLevelCode", "Shkalla e gjykimit nuk ekziston.");
            }
        }

        CourtDto? court = null;
        if (req.CourtId is { } courtId)
        {
            court = courtId is > 0 and <= ushort.MaxValue ? await reference.GetCourt((ushort)courtId, ct) : null;
            if (court is null || !court.IsActive)
            {
                errors.Add("courtId", "Gjykata nuk ekziston ose nuk është aktive.");
                court = null;
            }
            else if (level is not null && court.CourtLevelId != level.Id)
            {
                errors.Add("courtId", "Gjykata nuk i përket shkallës së gjykimit të zgjedhur.");
            }
        }

        JudgeDto? judge = null;
        if (req.JudgeId is { } judgeId)
        {
            judge = judgeId is > 0 and <= uint.MaxValue ? await reference.GetJudge((uint)judgeId, ct) : null;
            if (judge is null || !judge.IsActive)
            {
                errors.Add("judgeId", $"Gjyqtari nuk u gjet ose nuk është aktiv. {RegisterCaseValidator.JudgeMissingHint}");
                judge = null;
            }
            else if (court is not null && judge.CourtId != court.Id)
            {
                // the KLGJ list may be outdated: a warning, not an error
                warnings.Add(new RegistrationWarning("judge.court_mismatch",
                    $"Gjyqtari {judge.FirstName} {judge.LastName} figuron në listë në një gjykatë tjetër. Kontrolloni të dhënat."));
            }
        }

        SummonsTypeDto? summonsType = null;
        if (!string.IsNullOrWhiteSpace(req.SummonsTypeCode))
        {
            summonsType = await reference.GetSummonsType(req.SummonsTypeCode.Trim(), ct);
            if (summonsType is null || !summonsType.IsActive)
            {
                errors.Add("summonsTypeCode", "Lloji i fletëthirrjes nuk ekziston ose nuk është aktiv.");
            }
        }

        HearingTypeDto? hearingType = null;
        if (req.FirstHearing is not null)
        {
            var code = string.IsNullOrWhiteSpace(req.FirstHearing.HearingTypeCode)
                ? DefaultHearingTypeCode
                : req.FirstHearing.HearingTypeCode.Trim();
            hearingType = await reference.GetHearingType(code, ct);
            if (hearingType is null || !hearingType.IsActive)
            {
                errors.Add("firstHearing.hearingTypeCode", "Lloji i seancës nuk ekziston ose nuk është aktiv.");
            }
            else if (hearingType.IsOther && string.IsNullOrWhiteSpace(req.FirstHearing.OtherTypeText))
            {
                errors.Add("firstHearing.otherTypeText", "Përshkruani llojin e seancës.");
            }
        }

        // Only read when there are no errors, so the nulls never escape.
        return new Lookups(level!, court!, judge!, summonsType, hearingType!);
    }

    private async Task<(int Total, List<PossibleDuplicate> Visible)> FindPossibleDuplicatesAsync(
        ushort officeId, string summonsNumber, ushort courtId, string actNumber, CancellationToken ct)
    {
        var matches = await db.Cases.AsNoTracking()
            .Where(c => (c.OfficeId == officeId && c.LifecycleStatus != Case.Closed
                         && c.SummonsNumber == summonsNumber && c.CourtId == courtId)
                        || c.ActNumber == actNumber)
            .OrderByDescending(c => c.RegisteredAt)
            .Select(c => new { c.Id, c.CaseCode, c.LifecycleStatus, c.CourtId, c.RegisteredAt })
            .ToListAsync(ct);

        var visible = new List<PossibleDuplicate>();
        foreach (var m in matches)
        {
            if (await access.CanViewAsync((long)m.Id, ct))
            {
                var court = await reference.GetCourt(m.CourtId, ct);
                visible.Add(new PossibleDuplicate(m.CaseCode, m.LifecycleStatus, court?.Name ?? string.Empty, m.RegisteredAt));
            }
        }

        return (matches.Count, visible);
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
