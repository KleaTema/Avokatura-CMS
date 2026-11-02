namespace Ash.Modules.Cases.Application.AssignJurist;

/// <summary>A jurist of the user's institution; <see cref="IsCurrent"/> = already the institution's jurist on this case.</summary>
public sealed record AssignableJurist(Guid PublicId, string FullName, bool IsCurrent);

public sealed record JuristRef(Guid PublicId, string Name);

/// <summary>The new row version, the jurist now assigned and the one replaced (if any).</summary>
public sealed record AssignJuristResponse(uint RowVersion, JuristRef Jurist, JuristRef? Previous);
