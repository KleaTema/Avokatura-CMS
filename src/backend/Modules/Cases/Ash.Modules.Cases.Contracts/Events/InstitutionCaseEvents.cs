using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// An institution user assigned (or replaced) the institution's jurist on a case. Handlers run in the same transaction.
/// Notifications (task 040) tell the jurist, and the jurist they replace.
/// </summary>
public sealed record JuristAssigned(
    Guid CasePublicId, Guid InstitutionPublicId, Guid JuristUserPublicId, Guid? PreviousJuristUserPublicId) : IEvent;

/// <summary>
/// The primary lawyer delegated (DELEGIM) or related (RELATIM) the case to an institution, which now manages it.
/// Notifications (task 040) tell the institution's users and its jurist on the case.
/// </summary>
public sealed record CaseDelegated(Guid CasePublicId, Guid InstitutionPublicId, string Mode) : IEvent;

/// <summary>The delegation ended: the institution monitors the case again. Notified like <see cref="CaseDelegated"/>.</summary>
public sealed record CaseUndelegated(Guid CasePublicId, Guid InstitutionPublicId) : IEvent;
