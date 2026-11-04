using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// A level was closed by recording its decision (task 036): POST /api/cases/{id}/decision, in that request's
/// transaction. ResultCode is the decision_results code; DecisionReceived false = closed while the reasoned decision
/// is still awaited. Notifications (task 040) tell the current team except the one who closed it.
/// Its reminders stop by themselves: the jobs of task 041 only read cases that are not CLOSED (and cancelled hearings
/// are no longer SCHEDULED), so nothing is stored to cancel.
/// </summary>
public sealed record CaseClosed(
    Guid CasePublicId, string CaseCode, string ResultCode, bool DecisionReceived, bool IsFinal, DateTime ClosedAt) : IEvent;
