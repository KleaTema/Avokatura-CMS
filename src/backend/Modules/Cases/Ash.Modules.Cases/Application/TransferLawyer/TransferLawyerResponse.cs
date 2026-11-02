namespace Ash.Modules.Cases.Application.TransferLawyer;

public sealed record LawyerRef(Guid PublicId, string Name);

/// <summary>The case's lawyers after the transfer; <see cref="RowVersion"/> is the new value for the next change.</summary>
public sealed record TransferLawyerResponse(Guid CasePublicId, LawyerRef Primary, LawyerRef? Secondary, uint RowVersion);
