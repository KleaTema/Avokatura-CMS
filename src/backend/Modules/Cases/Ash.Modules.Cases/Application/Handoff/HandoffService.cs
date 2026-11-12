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

namespace Ash.Modules.Cases.Application.Handoff;

/// <summary>Where a handoff goes, as the user chose it (closing dialog, or a correction).</summary>
public sealed record HandoffInput(string? NextCourtLevelCode, int? NextCourtId, Guid? ReceivingOfficePublicId, string? Reason)
{
    public bool IsEmpty => NextCourtLevelCode is null && NextCourtId is null && ReceivingOfficePublicId is null && Reason is null;
}

/// <summary>A validated destination: the level, a court of that level, and the receiving office.</summary>
internal sealed record HandoffDestination(CourtLevelDto Level, CourtDto Court, OfficeInfo Office, string? OverrideReason);

/// <summary>
/// "Përcjellja e çështjes" (task 053c, client decision B.3, default D5). Closing a level that is NOT final hands it
/// to the next court (any level — a retrial goes to a lower one) and to the office that handles that court
/// (office_courts). An unmapped court needs the office chosen by hand plus a reason. All writes in the caller's
/// transaction, each with its history row and event (the receiving Protokoll are told by the Notifications module).
/// </summary>
internal sealed class HandoffService(
    CasesDbContext db,
    IReferenceData reference,
    IOfficeDirectory offices,
    IHistoryWriter history,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public const string MissingMessage = "Zgjidhni shkallën dhe gjykatën ku përcillet çështja.";
    public const string FinalMessage = "Vendimi me formë të prerë nuk përcillet në shkallë tjetër: hiqni shkallën dhe gjykatën.";
    public const string FinalReason = "Vendimi mori formë të prerë";
    public const string ChangedReason = "Destinacioni i përcjelljes u ndryshua";
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;

    /// <summary>
    /// The destination of a closing: required when not final (400 with <see cref="MissingMessage"/>), refused when
    /// final (400). Null (success) for a final decision.
    /// </summary>
    public async Task<Result<HandoffDestination?>> ForClosingAsync(HandoffInput input, bool isFinal, CancellationToken ct)
    {
        if (isFinal)
        {
            if (!input.IsEmpty)
            {
                return Error.Validation("handoff.not_for_final", FinalMessage);
            }

            return (HandoffDestination?)null;
        }

        var destination = await ResolveAsync(input, ct);
        if (destination.IsFailure)
        {
            return destination.Error!;
        }

        return destination.Value;
    }

    /// <summary>Validates level, court (of that level) and the receiving office (mapped, or chosen with a reason).</summary>
    public async Task<Result<HandoffDestination>> ResolveAsync(HandoffInput input, CancellationToken ct)
    {
        var errors = new FieldErrors();
        CourtLevelDto? level = null;
        if (string.IsNullOrWhiteSpace(input.NextCourtLevelCode))
        {
            errors.Add("nextCourtLevelCode", "Zgjidhni shkallën e radhës.");
        }
        else if ((level = await reference.GetCourtLevelByCode(input.NextCourtLevelCode.Trim(), ct)) is null)
        {
            errors.Add("nextCourtLevelCode", "Shkalla nuk ekziston.");
        }

        CourtDto? court = null;
        if (input.NextCourtId is not { } courtId)
        {
            errors.Add("nextCourtId", "Zgjidhni gjykatën.");
        }
        else if (courtId is <= 0 or > ushort.MaxValue || (court = await reference.GetCourt((ushort)courtId, ct)) is null || !court.IsActive)
        {
            errors.Add("nextCourtId", "Gjykata nuk ekziston.");
        }
        else if (level is not null && court.CourtLevelId != level.Id)
        {
            errors.Add("nextCourtId", "Gjykata nuk është e kësaj shkalle.");
        }

        if (errors.Any)
        {
            return errors.ToError(MissingMessage);
        }

        var mapped = await offices.GetByCourtAsync(court!.Id, ct);
        if (mapped is not null)
        {
            // the court decides the office; a hand-picked office is only for courts no office covers
            if (input.ReceivingOfficePublicId is { } chosen && chosen != mapped.PublicId)
            {
                errors.Add("receivingOfficePublicId", $"Gjykata mbulohet nga {mapped.Name}; zyra nuk zgjidhet.");
                return errors.ToError(MissingMessage);
            }

            return new HandoffDestination(level!, court, mapped, null);
        }

        // unmapped: the office and a reason are required
        OfficeInfo? office = null;
        if (input.ReceivingOfficePublicId is not { } officePublicId)
        {
            errors.Add("receivingOfficePublicId", "Kjo gjykatë nuk i është caktuar asnjë zyre: zgjidhni zyrën që e merr.");
        }
        else if ((office = await offices.GetAsync(officePublicId, ct)) is null || !office.IsActive)
        {
            errors.Add("receivingOfficePublicId", "Zyra nuk ekziston ose nuk është aktive.");
        }

        var reason = input.Reason?.Trim();
        if (reason is null || reason.Length < MinReasonLength)
        {
            errors.Add("reason", $"Shkruani arsyen (të paktën {MinReasonLength} karaktere).");
        }
        else if (reason.Length > MaxReasonLength)
        {
            errors.Add("reason", $"Arsyeja mund të ketë deri në {MaxReasonLength} karaktere.");
        }

        return errors.Any ? errors.ToError(MissingMessage) : new HandoffDestination(level!, court, office!, reason);
    }

    /// <summary>The open handoff of a level (neither accepted nor cancelled), tracked; null when none.</summary>
    public Task<CaseHandoff?> OpenAsync(ulong fromCaseId, CancellationToken ct) =>
        db.CaseHandoffs.SingleOrDefaultAsync(h => h.FromCaseId == fromCaseId && h.AcceptedCaseId == null && h.CancelledAt == null, ct);

    /// <summary>Hands the closed level off: the row, CASE_HANDED_OFF, and the event for the receiving Protokoll.</summary>
    public async Task<CaseHandoff> CreateAsync(Case from, HandoffDestination to, CancellationToken ct)
    {
        var handoff = new CaseHandoff
        {
            PublicId = Guid.NewGuid(),
            DossierId = from.DossierId,
            FromCaseId = from.Id,
            ToOfficeId = (ushort)to.Office.Id,
            CourtLevelId = to.Level.Id,
            CourtId = to.Court.Id,
            OverrideReason = to.OverrideReason,
            CreatedBy = (uint)user.UserId,
            CreatedAt = clock.UtcNow,
        };
        db.CaseHandoffs.Add(handoff);
        await db.SaveChangesAsync(ct);
        history.Add((long)from.DossierId, (long)from.Id, HistoryEventType.CaseHandedOff,
            $"Çështja u përcoll në {to.Level.Name} — {to.Court.Name} ({to.Office.Name}).",
            @new: Snapshot(to));
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new CaseHandedOff(from.PublicId, from.CaseCode, to.Office.Id, to.Level.Name, to.Court.Name), ct);
        return handoff;
    }

    /// <summary>
    /// A new destination for the open handoff (task 053c: until it is accepted). Same destination → nothing happens.
    /// Otherwise the open row is cancelled and a new one created; HANDOFF_CHANGED; the old office is told when it
    /// changes, the new one is told.
    /// </summary>
    public async Task ChangeAsync(Case from, CaseHandoff open, HandoffDestination to, CancellationToken ct)
    {
        if (open.ToOfficeId == to.Office.Id && open.CourtId == to.Court.Id && open.CourtLevelId == to.Level.Id)
        {
            return;
        }

        var oldCourt = await reference.GetCourt(open.CourtId, ct);
        var oldOffice = await offices.GetByIdAsync(open.ToOfficeId, ct);
        open.CancelledAt = clock.UtcNow;
        open.CancelledBy = (uint)user.UserId;
        open.CancelReason = ChangedReason;
        await db.SaveChangesAsync(ct); // frees uq_cho_open for the new row

        var handoff = new CaseHandoff
        {
            PublicId = Guid.NewGuid(),
            DossierId = from.DossierId,
            FromCaseId = from.Id,
            ToOfficeId = (ushort)to.Office.Id,
            CourtLevelId = to.Level.Id,
            CourtId = to.Court.Id,
            OverrideReason = to.OverrideReason,
            CreatedBy = (uint)user.UserId,
            CreatedAt = clock.UtcNow,
        };
        db.CaseHandoffs.Add(handoff);
        await db.SaveChangesAsync(ct);
        history.Add((long)from.DossierId, (long)from.Id, HistoryEventType.HandoffChanged,
            $"Përcjellja u ndryshua: {to.Level.Name} — {to.Court.Name} ({to.Office.Name}).",
            old: new { Court = oldCourt?.Name, Office = oldOffice?.Name },
            @new: Snapshot(to));
        await db.SaveChangesAsync(ct);

        if (open.ToOfficeId != to.Office.Id)
        {
            await events.PublishAsync(new CaseHandoffWithdrawn(from.PublicId, from.CaseCode, open.ToOfficeId, ChangedReason), ct);
        }

        await events.PublishAsync(new CaseHandedOff(from.PublicId, from.CaseCode, to.Office.Id, to.Level.Name, to.Court.Name), ct);
    }

    /// <summary>The decision became final (037): the open handoff is cancelled by the system and its office told.</summary>
    public async Task CancelBecauseFinalAsync(Case from, CancellationToken ct)
    {
        if (await OpenAsync(from.Id, ct) is not { } open)
        {
            return;
        }

        open.CancelledAt = clock.UtcNow;
        open.CancelReason = FinalReason;
        await db.SaveChangesAsync(ct);
        history.Add((long)from.DossierId, (long)from.Id, HistoryEventType.HandoffCancelled,
            $"Përcjellja u anulua: {FinalReason.ToLowerInvariant()}.", @new: new { Reason = FinalReason });
        await db.SaveChangesAsync(ct);
        await events.PublishAsync(new CaseHandoffWithdrawn(from.PublicId, from.CaseCode, open.ToOfficeId, FinalReason), ct);
    }

    /// <summary>X/n+1 was registered from this level: its open handoff is accepted (it leaves "Çështjet e ardhura").</summary>
    public async Task AcceptAsync(ulong fromCaseId, ulong nextCaseId, CancellationToken ct)
    {
        if (await OpenAsync(fromCaseId, ct) is { } open)
        {
            open.AcceptedCaseId = nextCaseId;
            open.AcceptedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private static object Snapshot(HandoffDestination to) => new
    {
        CourtLevel = to.Level.Name,
        Court = to.Court.Name,
        Office = to.Office.Name,
        Reason = to.OverrideReason,
    };
}
