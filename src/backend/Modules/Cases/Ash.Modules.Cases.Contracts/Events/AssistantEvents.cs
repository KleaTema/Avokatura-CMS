using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// The primary lawyer added a legal assistant to the case (task 053q). Handlers run in the same transaction:
/// the assistant is notified and the chat follows the team.
/// </summary>
public sealed record AssistantAssigned(Guid CasePublicId, Guid AssistantUserPublicId) : IEvent;

/// <summary>The primary lawyer removed a legal assistant from the case (the assignment row is ended, not deleted).</summary>
public sealed record AssistantRemoved(Guid CasePublicId, Guid AssistantUserPublicId) : IEvent;
