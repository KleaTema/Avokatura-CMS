namespace Ash.Modules.Cases.Application.AssignLawyers;

/// <summary>Current PRIMARY / SECONDARY assignments of a lawyer on cases that are not CLOSED.</summary>
public sealed record LawyerWorkload(int ActivePrimary, int ActiveSecondary);

/// <summary>
/// A lawyer for the assignment / transfer pickers of one case. CanBePrimary: holds "Avokat Shteti (primar)";
/// CanBeSecondary: holds "Avokat sekondar" (task 053i) — the primary picker shows the first, the secondary the second.
/// </summary>
public sealed record AssignableLawyer(
    Guid PublicId, string FullName, bool IsSelf, LawyerWorkload Workload, bool CanBePrimary, bool CanBeSecondary);
