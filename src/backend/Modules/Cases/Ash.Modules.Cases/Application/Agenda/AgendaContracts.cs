using Ash.Modules.Cases.Application.GetCase;

namespace Ash.Modules.Cases.Application.Agenda;

/// <summary>
/// Query string of GET /api/agenda. from/to are Tirane calendar dates (both included); lawyerId is a user public id,
/// officeId an office public id; CANCELLED hearings only with includeCancelled=true.
/// </summary>
public sealed record AgendaQuery(DateOnly? From, DateOnly? To, Guid? LawyerId, Guid? OfficeId, bool? IncludeCancelled);

/// <summary>
/// One hearing of the agenda. Conflict: another non-cancelled hearing of the same lawyer within 60 minutes in the
/// range; ConflictWith lists the case codes of those hearings.
/// </summary>
public sealed record AgendaItem(
    Guid HearingPublicId,
    Guid CasePublicId,
    string CaseCode,
    DateTime ScheduledAt,
    CodeName HearingType,
    string? OtherTypeText,
    string Status,
    string CourtName,
    string? PrimaryLawyerName,
    bool IsOverdue,
    bool Conflict,
    IReadOnlyList<string> ConflictWith);

/// <summary>Whose hearings: the user's own cases, the office's cases (Kryetar), or all (Monitor/Admin).</summary>
public enum AgendaScope
{
    Own,
    Office,
    All
}

/// <summary>A parsed, validated agenda request: a UTC half-open range and the resolved internal ids.</summary>
public sealed record AgendaFilter(
    AgendaScope Scope,
    DateTime FromUtc,
    DateTime ToUtc,
    uint? LawyerUserId,
    ushort? OfficeId,
    bool IncludeCancelled);

/// <summary>A hearing row as the query returns it (internal ids; names are added afterwards).</summary>
public sealed record AgendaRow(
    ulong HearingId,
    Guid HearingPublicId,
    ulong CaseId,
    Guid CasePublicId,
    string CaseCode,
    DateTime ScheduledAt,
    byte HearingTypeId,
    string? OtherTypeText,
    string Status,
    ushort CourtId);
