using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Application.UpdateCaseData;

/// <summary>PATCH claim (case.edit_data): amount + currency and/or m², with CLAIM_CHANGED (old/new as strings).</summary>
internal sealed class UpdateClaimHandler(CaseDataGate gate, FirstDataEntry firstEntry, IHistoryWriter history, IAuditLog audit)
{
    public const string Permission = "case.edit_data";

    public async Task<Result<UpdateClaimResponse>> Handle(Guid casePublicId, UpdateClaimRequest req, CancellationToken ct)
    {
        var parsed = UpdateClaimValidator.Validate(req);
        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        var found = await gate.ForChangeAsync(casePublicId, req.RowVersion!.Value, [Permission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var next = parsed.Value;
        if (c.ClaimAmount == next.Amount && c.ClaimCurrency == next.Currency && c.ClaimAreaM2 == next.AreaM2)
        {
            return CaseDataGate.NoChange();
        }

        var old = Snapshot(c.ClaimAmount, c.ClaimCurrency, c.ClaimAreaM2);
        c.ClaimAmount = next.Amount;
        c.ClaimCurrency = next.Currency;
        c.ClaimAreaM2 = next.AreaM2;
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.ClaimChanged, "Vlera e padisë u ndryshua.",
            old: old, @new: Snapshot(c.ClaimAmount, c.ClaimCurrency, c.ClaimAreaM2));
        if (await gate.SaveAsync(c, ct) is { } conflict)
        {
            return conflict;
        }

        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id, details: new { Action = "UPDATE_CLAIM" });
        return new UpdateClaimResponse(c.RowVersion, DecimalText.Format(c.ClaimAmount), c.ClaimCurrency, DecimalText.Format(c.ClaimAreaM2));
    }

    // strings, never JSON numbers: the history keeps the exact value
    private static object Snapshot(decimal? amount, string? currency, decimal? area) =>
        new { ClaimAmount = DecimalText.Format(amount), ClaimCurrency = currency, ClaimAreaM2 = DecimalText.Format(area) };
}
