namespace Ash.Modules.Cases.Application.Assistants;

/// <summary>POST /api/cases/{id}/assistants. TODO(client-q5): several assistants per case are allowed (default).</summary>
public sealed record AddAssistantRequest(Guid? UserPublicId, uint? RowVersion);
