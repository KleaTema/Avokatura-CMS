using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Cases.Application.SetSuspension;

/// <summary>PATCH suspension (case.set_status): suspended_at = now when suspending, NULL when resuming; SUSPENSION_CHANGED.</summary>
internal sealed class SetSuspensionHandler(
    CaseDataGate gate, FirstDataEntry firstEntry, IHistoryWriter history, IAuditLog audit, IClock clock)
{
    public const string Permission = "case.set_status";

    public async Task<Result<SetSuspensionResponse>> Handle(Guid casePublicId, SetSuspensionRequest req, CancellationToken ct)
    {
        var invalid = SetSuspensionValidator.Validate(req);
        if (invalid is not null)
        {
            return invalid;
        }

        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, [Permission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var suspend = req.Suspended!.Value;
        if (c.IsSuspended == suspend)
        {
            return CaseDataGate.NoChange();
        }

        var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
        c.IsSuspended = suspend;
        c.SuspendedAt = suspend ? clock.UtcNow : null;
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.SuspensionChanged,
            suspend ? "Çështja u pezullua." : "Çështja rifilloi (pezullimi u hoq).",
            old: new { IsSuspended = !suspend }, @new: new { IsSuspended = suspend, Reason = reason });
        if (await gate.SaveAsync(c, ct) is { } conflict)
        {
            return conflict;
        }

        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id, details: new { Action = "SET_SUSPENSION", Suspended = suspend });
        return new SetSuspensionResponse(c.RowVersion, c.IsSuspended, c.SuspendedAt);
    }
}
