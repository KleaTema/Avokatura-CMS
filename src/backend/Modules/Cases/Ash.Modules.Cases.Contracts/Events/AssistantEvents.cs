using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// The primary lawyer added a legal assistant to the case. Handlers run in the same transaction.
/// TODO(task-040): Notifications tell the assistant.
/// </summary>
public sealed record AssistantAssigned(Guid CasePublicId, Guid AssistantUserPublicId) : IEvent;

/// <summary>The primary lawyer removed a legal assistant from the case (the assignment row is ended, not deleted).</summary>
public sealed record AssistantRemoved(Guid CasePublicId, Guid AssistantUserPublicId) : IEvent;
