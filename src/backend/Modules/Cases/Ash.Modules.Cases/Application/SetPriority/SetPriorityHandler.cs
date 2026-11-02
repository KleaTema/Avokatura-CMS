using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.SetPriority;

/// <summary>
/// PATCH priority: case.set_priority (the primary lawyer) or case.set_priority_any (the Kryetar of the case's office).
/// PRIORITY_CHANGED with old/new, the same shape as at the assignment (task 023).
/// </summary>
internal sealed class SetPriorityHandler(CaseDataGate gate, FirstDataEntry firstEntry, IHistoryWriter history, IAuditLog audit)
{
    public static readonly string[] Permissions = ["case.set_priority", AssignLawyersHandler.SetPriorityPermission];

    public async Task<Result<SetPriorityResponse>> Handle(Guid casePublicId, SetPriorityRequest req, CancellationToken ct)
    {
        var invalid = SetPriorityValidator.Validate(req);
        if (invalid is not null)
        {
            return invalid;
        }

        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, Permissions, ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        if (c.Priority == req.Priority)
        {
            return CaseDataGate.NoChange();
        }

        var old = c.Priority;
        c.Priority = req.Priority!;
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.PriorityChanged,
            $"Prioriteti u ndryshua: {AssignLawyersHandler.PriorityNames[old]} → {AssignLawyersHandler.PriorityNames[c.Priority]}.",
            old: new { Priority = old }, @new: new { c.Priority });
        if (await gate.SaveAsync(c, ct) is { } conflict)
        {
            return conflict;
        }

        // a Kryetar who is not on the team does not count as the team's first data entry (FirstDataEntry decides)
        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id, details: new { Action = "SET_PRIORITY", c.Priority });
        return new SetPriorityResponse(c.RowVersion, c.Priority);
    }
}
