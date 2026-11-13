using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// Task 053d: the Monitor put a lawyer's cases (<paramref name="LawyerName"/>) or one case (<paramref name="CasePublicId"/>)
/// under the Kryetar's supervision. The Kryetar gets a mandatory notification.
/// </summary>
public sealed record SupervisionStarted(Guid HeadUserPublicId, string? LawyerName, Guid? CasePublicId) : IEvent;

/// <summary>Task 053d: the supervision ended; the Kryetar is told.</summary>
public sealed record SupervisionEnded(Guid HeadUserPublicId, string? LawyerName, Guid? CasePublicId) : IEvent;
