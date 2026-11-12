using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// Task 053c: a closed level that is not final was handed off to the next court and its office (on closing, or by a
/// correction). The Protokoll of <paramref name="ToOfficeId"/> are told (notification + e-mail). Internal office id:
/// for the recipients only, never sent out.
/// </summary>
public sealed record CaseHandedOff(
    Guid CasePublicId, string CaseCode, long ToOfficeId, string CourtLevelName, string CourtName) : IEvent;

/// <summary>
/// Task 053c: an open handoff no longer goes to <paramref name="OfficeId"/> — corrected to another destination, or
/// cancelled because the decision became final. Its Protokoll are told, with the reason.
/// </summary>
public sealed record CaseHandoffWithdrawn(Guid CasePublicId, string CaseCode, long OfficeId, string Reason) : IEvent;
