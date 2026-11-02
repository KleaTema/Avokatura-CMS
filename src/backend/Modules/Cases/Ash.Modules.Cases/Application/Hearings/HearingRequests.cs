namespace Ash.Modules.Cases.Application.Hearings;

/// <summary>POST /api/cases/{id}/hearings — at: ISO 8601 with an offset (stored UTC); type by code.</summary>
public sealed record AddHearingRequest(string? At, string? HearingTypeCode, string? OtherTypeText, string? Description);

/// <summary>
/// PUT /api/cases/{id}/hearings/{hearingId} — every field optional: a SCHEDULED hearing may be rescheduled and its type
/// and notes changed; any hearing's description may be corrected. The status is never changed here.
/// </summary>
public sealed record UpdateHearingRequest(
    string? At, string? HearingTypeCode, string? OtherTypeText, string? Description, uint? RowVersion);

/// <summary>The hearing scheduled in the same step as completing the previous one.</summary>
public sealed record NextHearingRequest(string? At, string? HearingTypeCode, string? OtherTypeText);

/// <summary>
/// POST /api/cases/{id}/hearings/{hearingId}/complete — outcome HELD | POSTPONED | CANCELLED (only from SCHEDULED);
/// description required for HELD and POSTPONED; optionally the next hearing.
/// </summary>
public sealed record CompleteHearingRequest(string? Outcome, string? Description, NextHearingRequest? Next, uint? RowVersion);
