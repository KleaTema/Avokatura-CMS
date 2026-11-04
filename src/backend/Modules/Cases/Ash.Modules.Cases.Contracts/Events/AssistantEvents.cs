using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// The primary lawyer added a legal assistant to the case. Handlers run in the same transaction.
/// Notifications (task 040) tell the assistant — on removal too.
/// </summary>
public sealed record AssistantAssigned(Guid CasePublicId, Guid AssistantUserPublicId) : IEvent;

/// <summary>The primary lawyer removed a legal assistant from the case (the assignment row is ended, not deleted).</summary>
public sealed record AssistantRemoved(Guid CasePublicId, Guid AssistantUserPublicId) : IEvent;
