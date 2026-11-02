namespace Ash.Modules.Cases.Application.UpdateCaseData;

/// <summary>
/// PATCH /api/cases/{id}/claim — the whole claim value: amount (string decimal, DECIMAL(18,2)) with its currency
/// (ISO 4217, "ALL" when omitted) and/or m² (string decimal, DECIMAL(14,2)); null clears a part.
/// </summary>
public sealed record UpdateClaimRequest(string? ClaimAmount, string? ClaimCurrency, string? ClaimAreaM2, uint? RowVersion);
