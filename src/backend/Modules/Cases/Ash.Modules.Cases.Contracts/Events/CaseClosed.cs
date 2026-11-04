using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// A level was closed by recording its decision (task 036): POST /api/cases/{id}/decision, in that request's
/// transaction. ResultCode is the decision_results code; DecisionReceived false = closed while the reasoned decision
/// is still awaited. Notifications (task 040) tell the current team except the one who closed it.
/// TODO(task-041): cancel the reminders of the case (hearings, the 30-day data-entry reminder).
/// </summary>
public sealed record CaseClosed(
    Guid CasePublicId, string CaseCode, string ResultCode, bool DecisionReceived, bool IsFinal, DateTime ClosedAt) : IEvent;
