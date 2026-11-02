using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.ChangeObject;

/// <summary>
/// PATCH object (case.change_object: primary, secondary, assistant, delegated institution, the registering Protokoll
/// while REGISTERED). OBJECT_CHANGED keeps the full old and new text.
/// </summary>
internal sealed class ChangeObjectHandler(CaseDataGate gate, FirstDataEntry firstEntry, IHistoryWriter history, IAuditLog audit)
{
    public const string Permission = "case.change_object";

    public async Task<Result<ChangeObjectResponse>> Handle(Guid casePublicId, ChangeObjectRequest req, CancellationToken ct)
    {
        var invalid = ChangeObjectValidator.Validate(req);
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
        var text = req.ObjectText!.Trim();
        if (text == c.ObjectText)
        {
            return CaseDataGate.NoChange();
        }

        var old = c.ObjectText;
        c.ObjectText = text;
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.ObjectChanged, "Objekti u ndryshua.",
            old: new { ObjectText = old }, @new: new { ObjectText = text });
        if (await gate.SaveAsync(c, ct) is { } conflict)
        {
            return conflict;
        }

        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id, details: new { Action = "CHANGE_OBJECT" });
        return new ChangeObjectResponse(c.RowVersion, c.ObjectText);
    }
}
