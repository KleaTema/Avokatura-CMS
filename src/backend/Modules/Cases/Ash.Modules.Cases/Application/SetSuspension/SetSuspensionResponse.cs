namespace Ash.Modules.Cases.Application.SetSuspension;

public sealed record SetSuspensionResponse(uint RowVersion, bool IsSuspended, DateTime? SuspendedAt);
