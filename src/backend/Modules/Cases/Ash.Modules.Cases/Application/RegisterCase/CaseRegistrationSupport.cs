using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.RegisterCase;

/// <summary>The resolved lookups of a registration form; only read when there were no field errors.</summary>
internal sealed record RegistrationLookups(
    CourtLevelDto CourtLevel, CourtDto Court, JudgeDto Judge, SummonsTypeDto? SummonsType, HearingTypeDto HearingType);

/// <summary>
/// Steps shared by registering a new case (task 016) and continuing a closed one at the next level (task 018):
/// lookups, the office head check, the possible-duplicate check and the first hearing.
/// </summary>
internal sealed class CaseRegistrationSupport(
    CasesDbContext db, IReferenceData reference, IOfficeDirectory offices, ICaseAccessService access, ICurrentUser user, IClock clock)
{
    public const string DefaultHearingTypeCode = "PERGATITORE";
    public const string InvalidMessage = "Të dhënat e regjistrimit nuk janë të vlefshme.";

    public async Task<RegistrationLookups> ResolveLookupsAsync(
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
        return new RegistrationLookups(level!, court!, judge!, summonsType, hearingType!);
    }

    public void WarnIfHearingInPast(DateTime? hearingUtc, List<RegistrationWarning> warnings)
    {
        if (hearingUtc < clock.UtcNow)
        {
            warnings.Add(new RegistrationWarning("hearing.in_past",
                "Data e seancës së parë është në të kaluarën. Kontrolloni nëse është regjistrim i vonuar."));
        }
    }

    /// <summary>
    /// The user's office must be active and have an active head, who receives the new case.
    /// Checked before anything is written (for a new dossier: before create_dossier, so no code is spent).
    /// </summary>
    public async Task<Result<(long OfficeId, OfficeHead Head)>> CheckOfficeAsync(CancellationToken ct)
    {
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

        return (officeId, head);
    }

    /// <summary>
    /// A non-closed case of the office with the same summons number and court, or any case with the same act number.
    /// Returns a 409 unless confirmed; <paramref name="exceptDossierId"/> leaves out the dossier being continued.
    /// </summary>
    public async Task<(int Total, Error? Conflict)> CheckPossibleDuplicatesAsync(
        ushort officeId, string summonsNumber, ushort courtId, string actNumber, bool confirmed, ulong? exceptDossierId,
        CancellationToken ct)
    {
        var matches = await db.Cases.AsNoTracking()
            .Where(c => (exceptDossierId == null || c.DossierId != exceptDossierId)
                        && ((c.OfficeId == officeId && c.LifecycleStatus != Case.Closed
                             && c.SummonsNumber == summonsNumber && c.CourtId == courtId)
                            || c.ActNumber == actNumber))
            .OrderByDescending(c => c.RegisteredAt)
            .Select(c => new { c.Id, c.CaseCode, c.LifecycleStatus, c.CourtId, c.RegisteredAt })
            .ToListAsync(ct);
        if (matches.Count == 0 || confirmed)
        {
            return (matches.Count, null);
        }

        var visible = new List<PossibleDuplicate>();
        foreach (var m in matches)
        {
            if (await access.CanViewAsync((long)m.Id, ct))
            {
                var court = await reference.GetCourt(m.CourtId, ct);
                visible.Add(new PossibleDuplicate(m.CaseCode, m.LifecycleStatus, court?.Name ?? string.Empty, m.RegisteredAt));
            }
        }

        // The same act number usually means the next level of an existing case: the UI offers the registration search (task 018).
        var conflict = Error.Conflict("case.possible_duplicate",
                "Ekziston tashmë një çështje me të njëjtin numër fletëthirrjeje dhe gjykatë, ose me të njëjtin numër akti. Konfirmoni nëse doni ta regjistroni gjithsesi.")
            with
            {
                Extensions = new Dictionary<string, object?>
                {
                    ["matches"] = visible,
                    ["hiddenCount"] = matches.Count - visible.Count
                }
            };
        return (matches.Count, conflict);
    }

    /// <summary>The first hearing (SCHEDULED) of a new case; the case's next_hearing_at is set by the caller.</summary>
    public void AddFirstHearing(Case target, RegistrationLookups lookups, FirstHearingRequest hearing, DateTime hearingUtc) =>
        db.CaseHearings.Add(new CaseHearing
        {
            CaseId = target.Id,
            HearingTypeId = lookups.HearingType.Id,
            OtherTypeText = lookups.HearingType.IsOther ? hearing.OtherTypeText!.Trim() : null,
            ScheduledAt = hearingUtc,
            Status = CaseHearing.Scheduled,
            CreatedBy = (uint)user.UserId
        });
}
