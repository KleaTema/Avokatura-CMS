using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.History;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.RegisterNextLevel;

/// <summary>The next-level summons: its number, the court that issued it and the hearing (ISO 8601 with offset).</summary>
public sealed record NotifyNotClosedRequest(string? SummonsNumber, int? CourtId, string? HearingAt);

public sealed record NotifyNotClosedResponse(bool AlreadyNotified, DateTime NotifiedAt);

/// <summary>
/// Protokoll has a next-level summons for a case whose level is not closed in the system (client answer Q1b:
/// Protokoll waits for the lawyer and never edits the previous level). Records it on that case
/// (NEXT_LEVEL_SUMMONS_PENDING; the case row itself is not changed) and publishes PreviousLevelNotClosed to the
/// primary lawyer and the office head — in the normal, committed transaction. The same summons number is announced
/// at most once per case per 24 hours; a different summons number is another summons and is announced too.
/// </summary>
internal sealed class NotifyNotClosedHandler(
    CasesDbContext db,
    IUnitOfWork unitOfWork,
    ICaseAccessService access,
    IReferenceData reference,
    IUserDirectory users,
    IOfficeDirectory offices,
    IHistoryWriter history,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public async Task<Result<NotifyNotClosedResponse>> Handle(Guid casePublicId, NotifyNotClosedRequest req, CancellationToken ct)
    {
        var target = await db.Cases.AsNoTracking().SingleOrDefaultAsync(c => c.PublicId == casePublicId, ct);
        if (target is null || access.GetRegistrationLookupScope() == RegistrationLookupScope.None)
        {
            return Error.NotFound("case.not_found", "Çështja nuk u gjet.");
        }

        var latest = await db.Cases.AsNoTracking()
            .Where(c => c.DossierId == target.DossierId)
            .OrderByDescending(c => c.LevelSeq)
            .Select(c => new { c.Id, c.CaseCode })
            .FirstAsync(ct);
        if (latest.Id != target.Id)
        {
            return RegisterNextLevelHandler.AlreadyContinued(latest.CaseCode);
        }

        if (target.LifecycleStatus == Case.Closed)
        {
            return Error.Conflict("case.previous_closed",
                "Kjo shkallë është mbyllur; regjistroni shkallën e radhës në vend të njoftimit.");
        }

        // The summons, validated like a registration
        var errors = new FieldErrors();
        if (string.IsNullOrWhiteSpace(req.SummonsNumber) || req.SummonsNumber.Trim().Length > RegisterCaseValidator.MaxNumberLength)
        {
            errors.Add("summonsNumber", $"Numri i fletëthirrjes është i detyrueshëm (deri në {RegisterCaseValidator.MaxNumberLength} karaktere).");
        }

        CourtDto? court = null;
        if (req.CourtId is not { } courtId)
        {
            errors.Add("courtId", "Gjykata është e detyrueshme.");
        }
        else
        {
            court = courtId is > 0 and <= ushort.MaxValue ? await reference.GetCourt((ushort)courtId, ct) : null;
            if (court is not { IsActive: true })
            {
                errors.Add("courtId", "Gjykata nuk ekziston ose nuk është aktive.");
            }
        }

        var hearingUtc = RegisterCaseValidator.ParseHearingAt(req.HearingAt, "hearingAt", errors);
        if (errors.Any)
        {
            return errors.ToError("Të dhënat e fletëthirrjes nuk janë të vlefshme.");
        }

        var summonsNumber = req.SummonsNumber!.Trim();

        // Serialize notifications of the same case, then check for one already made: two parallel requests for the
        // same summons give one history row and one event.
        await LockCaseAsync(target.Id, ct);
        var now = clock.UtcNow;
        if (await LastNoticeAsync(target.Id, summonsNumber, now - Window, ct) is { } notifiedAt)
        {
            return new NotifyNotClosedResponse(true, notifiedAt);
        }

        var me = await users.GetUser(user.PublicId, ct);
        var name = me is null ? "Protokolli" : $"{me.FirstName} {me.LastName}";
        var hearingLocal = TimeZoneInfo.ConvertTimeFromUtc(hearingUtc!.Value, TiraneTime.Zone).ToString("dd.MM.yyyy HH:mm");
        history.Add((long)target.DossierId, (long)target.Id, HistoryEventType.NextLevelSummonsPending,
            $"Erdhi fletëthirrja nr. {summonsNumber} ({court!.Name}, seanca më {hearingLocal}) për shkallën e radhës, " +
            $"por kjo shkallë nuk është mbyllur ende (njoftoi {name}).",
            @new: new { SummonsNumber = summonsNumber, Court = court.Name, HearingAt = hearingUtc.Value, NotifiedBy = name });
        await db.SaveChangesAsync(ct);

        var (primaryLawyer, head) = await RecipientsAsync(target, ct);
        await events.PublishAsync(new PreviousLevelNotClosed(target.PublicId, summonsNumber, hearingUtc.Value, primaryLawyer, head), ct);
        return new NotifyNotClosedResponse(false, now);
    }

    private async Task LockCaseAsync(ulong caseId, CancellationToken ct)
    {
        await using var command = unitOfWork.CreateCommand("SELECT id FROM cases WHERE id = @id FOR UPDATE");
        command.Parameters.AddWithValue("@id", caseId);
        await command.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// A locking read (FOR SHARE), not a plain one: it sees the rows committed by a request that held the case lock
    /// before us. A plain read would use this transaction's snapshot, which is already taken (the user is resolved
    /// inside the same transaction, before the handler runs) and would miss them.
    /// </summary>
    private async Task<DateTime?> LastNoticeAsync(ulong caseId, string summonsNumber, DateTime since, CancellationToken ct)
    {
        await using var command = unitOfWork.CreateCommand("""
            SELECT occurred_at FROM case_history_events
            WHERE case_id = @case AND event_type = @type AND occurred_at >= @since
              AND JSON_UNQUOTE(JSON_EXTRACT(details, '$.new.summonsNumber')) = @summons
            ORDER BY occurred_at DESC
            LIMIT 1
            FOR SHARE
            """);
        command.Parameters.AddWithValue("@case", caseId);
        command.Parameters.AddWithValue("@type", HistoryEventCodes.ToCode(HistoryEventType.NextLevelSummonsPending));
        command.Parameters.AddWithValue("@since", since);
        command.Parameters.AddWithValue("@summons", summonsNumber);
        return await command.ExecuteScalarAsync(ct) is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null;
    }

    /// <summary>The current primary lawyer (if one is assigned) and the head of the case's office.</summary>
    private async Task<(Guid? PrimaryLawyer, Guid? Head)> RecipientsAsync(Case target, CancellationToken ct)
    {
        var lawyerId = await db.CaseAssignments.AsNoTracking()
            .Where(a => a.CaseId == target.Id && a.CaseRole == CaseAssignmentRow.PrimaryLawyer && a.EndedAt == null)
            .Select(a => (uint?)a.UserId)
            .FirstOrDefaultAsync(ct);
        Guid? lawyer = lawyerId is { } id && (await users.GetPublicIdsAsync([id], ct)).TryGetValue(id, out var publicId)
            ? publicId
            : null;
        var routing = await offices.GetRoutingAsync(target.OfficeId, ct);
        return (lawyer, routing?.Head is { IsActive: true } head ? head.PublicId : null);
    }
}
