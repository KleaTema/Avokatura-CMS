namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>Current PRIMARY / SECONDARY assignments of a lawyer on cases that are not CLOSED.</summary>
public sealed record LawyerWorkload(int ActivePrimary, int ActiveSecondary);

/// <summary>A lawyer for the assignment picker of one case.</summary>
public sealed record AssignableLawyer(Guid PublicId, string FullName, bool IsSelf, LawyerWorkload Workload);

/// <summary>A lawyer of the user's office, for the lawyer filter of the case list.</summary>
public sealed record OfficeLawyer(Guid PublicId, string FullName, bool IsSelf);
