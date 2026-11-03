using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.Hearings;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.RecordDecision;

/// <summary>
/// POST /api/cases/{id}/decision (task 036): records the decision and closes the level in ONE transaction. Checks,
/// in order: no view → 404; not ACTIVE → 409; no decision.record → 403; the fields → 400; the decision document →
/// 400; stale case rowVersion → 409; SCHEDULED hearings without cancelOpenHearings → 409 case.open_hearings (listed).
/// Then: the hearings CANCELLED (one history row each), the decision row, the case CLOSED (closed_at, closed_by,
/// next_hearing_at NULL, row_version + 1). The assignments are KEPT: the team still sees the closed case.
/// History DECISION_RECORDED and CASE_CLOSED; audit; CaseClosed published. Closing without the reasoned decision
/// (decisionReceived false) leaves decision_received = 0 — the case still counts as closed.
/// </summary>
internal sealed class RecordDecisionHandler(
    CasesDbContext db,
    ICaseAccessService access,
    DecisionSupport support,
    IReferenceData reference,
    IHistoryWriter history,
    IAuditLog audit,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public const string CancelledDescription = "Anuluar në mbyllje të çështjes";

    public async Task<Result<RecordDecisionResponse>> Handle(Guid casePublicId, DecisionRequest req, CancellationToken ct)
    {
        var c = await db.Cases.SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        if (c.LifecycleStatus == Case.Closed)
        {
            return Error.Conflict("decision.exists", "Vendimi i kësaj çështjeje është regjistruar; plotësojeni atë.");
        }

        if (c.LifecycleStatus != Case.Active)
        {
            return Error.Conflict("case.not_active", "Vendimi regjistrohet vetëm për një çështje aktive (me avokat të caktuar).");
        }

        if (!(await access.GetEffectivePermissionsAsync((long)c.Id, ct)).Contains(DecisionSupport.Permission))
        {
            return AssignLawyersHandler.Forbidden();
        }

        var parsed = await support.ValidateAsync(req, c, ct);
        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        var values = parsed.Value;
        long? documentId = null;
        if (values.DecisionDocumentPublicId is { } documentPublicId)
        {
            var document = await support.DocumentAsync(documentPublicId, c, ct);
            if (document.IsFailure)
            {
                return document.Error!;
            }

            documentId = document.Value;
        }

        if (c.RowVersion != req.RowVersion)
        {
            return AssignLawyersHandler.Changed();
        }

        // the hearings still SCHEDULED: listed (409) unless the user cancels them with the closing
        // TODO(client-q-close-hearings): confirm that closing may cancel the scheduled hearings
        var open = await db.CaseHearings
            .Where(h => h.CaseId == c.Id && h.Status == CaseHearing.Scheduled)
            .OrderBy(h => h.ScheduledAt)
            .ToListAsync(ct);
        if (open.Count > 0 && req.CancelOpenHearings != true)
        {
            return await OpenHearingsAsync(open, ct);
        }

        var now = clock.UtcNow;
        var userId = (uint)user.UserId;
        foreach (var hearing in open)
        {
            hearing.Status = CaseHearing.Cancelled;
            hearing.Description = CancelledDescription;
            hearing.UpdatedBy = userId;
            hearing.RowVersion++;
            history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.HearingCompleted,
                $"Seanca e {HearingWriter.Local(hearing.ScheduledAt)} u anulua në mbyllje të çështjes.",
                old: new { Status = CaseHearing.Scheduled },
                @new: new { Status = CaseHearing.Cancelled, hearing.Description });
        }

        var decision = new CaseDecision
        {
            CaseId = c.Id,
            RecordedBy = userId,
            RecordedAt = now,
            DecisionReceived = values.DecisionReceived,
            DecisionReceivedAt = values.DecisionReceived ? now : null,
            DecisionDocumentId = (ulong?)documentId,
        };
        DecisionSupport.Apply(decision, values);
        db.CaseDecisions.Add(decision);

        c.LifecycleStatus = Case.Closed;
        c.ClosedAt = now;
        c.ClosedBy = userId;
        c.NextHearingAt = null;
        c.RowVersion++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AssignLawyersHandler.Changed();
        }

        var snapshot = await support.SnapshotAsync(decision, ct);
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.DecisionRecorded,
            $"Vendimi u regjistrua: {values.Result.Name}" + (values.DecisionReceived ? "." : " (pa vendimin e arsyetuar)."),
            @new: snapshot);
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.CaseClosed, "Çështja u mbyll.",
            old: new { Status = Case.Active }, @new: new { Status = Case.Closed });
        await db.SaveChangesAsync(ct);

        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id,
            details: new { Action = "RECORD_DECISION", Result = values.Result.Code, values.DecisionReceived, CancelledHearings = open.Count });
        foreach (var hearing in open)
        {
            await events.PublishAsync(new HearingCompleted(c.PublicId, hearing.PublicId, CaseHearing.Cancelled, null), ct);
        }

        await events.PublishAsync(new CaseClosed(c.PublicId, c.CaseCode, values.Result.Code, values.DecisionReceived, values.IsFinal, now), ct);
        return new RecordDecisionResponse(c.RowVersion, now, open.Count, await support.ViewAsync(decision, ct),
            DecisionSupport.Suggestions(values.Result, values.IsFinal));
    }

    /// <summary>409 case.open_hearings with the hearings (time, type) in the ProblemDetails' "hearings".</summary>
    private async Task<Error> OpenHearingsAsync(List<CaseHearing> open, CancellationToken ct)
    {
        var types = await reference.ListHearingTypesAsync(ct);
        var list = open.Select(h =>
        {
            var type = types.FirstOrDefault(t => t.Id == h.HearingTypeId)?.Name ?? string.Empty;
            return new OpenHearingView(h.PublicId, h.ScheduledAt, h.OtherTypeText is { } other ? $"{type}: {other}" : type);
        }).ToList();
        var when = string.Join(", ", open.Select(h => HearingWriter.Local(h.ScheduledAt)));
        return Error.Conflict("case.open_hearings",
                $"Çështja ka seanca të planifikuara ({when}). Anulojini me mbylljen ose plotësojini më parë.")
            with { Extensions = new Dictionary<string, object?> { ["hearings"] = list } };
    }
}
