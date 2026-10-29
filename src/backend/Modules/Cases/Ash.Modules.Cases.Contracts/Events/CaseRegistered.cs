using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>A new case was registered and sent to the head of the office. Handlers run in the same transaction.</summary>
public sealed record CaseRegistered(Guid CasePublicId, long OfficeId, Guid HeadUserPublicId) : IEvent;
