using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// A legal assistant got a case (task 053e: AssistantSync, from the assistant's link to the case's primary lawyer).
/// Handlers run in the same transaction: the chat follows the team. The assistant is told once per link
/// (<see cref="AssistantLinked"/>), not per case.
/// </summary>
public sealed record AssistantAssigned(Guid CasePublicId, Guid AssistantUserPublicId) : IEvent;

/// <summary>A legal assistant lost a case (the assignment row is ended, not deleted) — unlink, transfer, reconcile.</summary>
public sealed record AssistantRemoved(Guid CasePublicId, Guid AssistantUserPublicId) : IEvent;

/// <summary>Task 053e: the Kryetar attached the assistant to a lawyer; the assistant is told (mandatory).</summary>
public sealed record AssistantLinked(Guid AssistantUserPublicId, string LawyerName, int Cases) : IEvent;

/// <summary>Task 053e: the Kryetar detached the assistant from a lawyer; the assistant is told (mandatory).</summary>
public sealed record AssistantUnlinked(Guid AssistantUserPublicId, string LawyerName) : IEvent;
