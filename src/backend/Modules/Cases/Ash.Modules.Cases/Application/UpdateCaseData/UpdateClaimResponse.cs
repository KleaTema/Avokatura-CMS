namespace Ash.Modules.Cases.Application.UpdateCaseData;

/// <summary>The stored claim (decimals as strings with two decimals) and the new row version for the next save.</summary>
public sealed record UpdateClaimResponse(uint RowVersion, string? ClaimAmount, string? ClaimCurrency, string? ClaimAreaM2);
