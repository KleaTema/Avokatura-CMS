namespace Ash.Modules.Cases.Application.DelegateCase;

/// <summary>POST /api/cases/{id}/delegate — mode is the delegation category's code: DELEGIM | RELATIM.</summary>
public sealed record DelegateCaseRequest(Guid? InstitutionPublicId, string? Mode, uint? RowVersion);

/// <summary>POST /api/cases/{id}/undelegate — the institution monitors the case again.</summary>
public sealed record UndelegateCaseRequest(string? Reason, uint? RowVersion);
