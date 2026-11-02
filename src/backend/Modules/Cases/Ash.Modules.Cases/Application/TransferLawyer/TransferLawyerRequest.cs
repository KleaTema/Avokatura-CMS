namespace Ash.Modules.Cases.Application.TransferLawyer;

/// <summary>
/// POST /api/cases/{id}/transfer. <see cref="NewPrimaryUserPublicId"/> null = the primary stays; <see cref="Secondary"/>
/// null = keep. <see cref="RowVersion"/> is the case's row_version as the client read it.
/// </summary>
public sealed record TransferLawyerRequest(
    Guid? NewPrimaryUserPublicId,
    SecondaryChangeRequest? Secondary,
    string? Reason,
    uint? RowVersion);

/// <summary>action: keep | set | remove; userPublicId only with set.</summary>
public sealed record SecondaryChangeRequest(string? Action, Guid? UserPublicId);
