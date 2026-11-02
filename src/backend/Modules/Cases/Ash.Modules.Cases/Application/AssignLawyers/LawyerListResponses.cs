namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>Current PRIMARY / SECONDARY assignments of a lawyer on cases that are not CLOSED.</summary>
public sealed record LawyerWorkload(int ActivePrimary, int ActiveSecondary);

/// <summary>A lawyer for the assignment picker of one case.</summary>
public sealed record AssignableLawyer(Guid PublicId, string FullName, bool IsSelf, LawyerWorkload Workload);
