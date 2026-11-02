namespace Ash.Modules.Cases.Application.AssignJurist;

/// <summary>POST /api/cases/{id}/jurist — the institution user's own institution's jurist for this case.</summary>
public sealed record AssignJuristRequest(Guid? UserPublicId, uint? RowVersion);
