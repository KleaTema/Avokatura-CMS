namespace Ash.Modules.Cases.Application.SetPriority;

/// <summary>PATCH /api/cases/{id}/priority — NONE | LOW | MEDIUM | HIGH.</summary>
public sealed record SetPriorityRequest(string? Priority, uint? RowVersion);
