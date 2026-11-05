using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// A dossier was archived after its latest level got a final decision (task 038): every level is read-only from now.
/// Published from POST /api/dossiers/{id}/archive, in that request's transaction. CasePublicIds: every level.
/// The Chat module (task 049) makes the case chats of these levels read-only after the commit.
/// </summary>
public sealed record DossierArchived(
    Guid DossierPublicId, string UniqueCode, IReadOnlyList<Guid> CasePublicIds, DateTime ArchivedAt) : IEvent;
