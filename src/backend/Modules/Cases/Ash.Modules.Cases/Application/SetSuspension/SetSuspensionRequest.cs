namespace Ash.Modules.Cases.Application.SetSuspension;

/// <summary>PATCH /api/cases/{id}/suspension — suspend (true) or resume (false), with an optional reason.</summary>
public sealed record SetSuspensionRequest(bool? Suspended, string? Reason, uint? RowVersion);
