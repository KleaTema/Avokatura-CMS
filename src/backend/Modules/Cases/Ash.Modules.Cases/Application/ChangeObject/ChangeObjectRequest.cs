namespace Ash.Modules.Cases.Application.ChangeObject;

/// <summary>PATCH /api/cases/{id}/object — the whole new object text (required, at most 4000 characters).</summary>
public sealed record ChangeObjectRequest(string? ObjectText, uint? RowVersion);

public sealed record ChangeObjectResponse(uint RowVersion, string ObjectText);
