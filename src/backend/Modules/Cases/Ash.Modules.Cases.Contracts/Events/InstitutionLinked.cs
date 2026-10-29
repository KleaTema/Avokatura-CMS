using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>An institution was linked to a case (MONITORING or DELEGATED); its users get notified. Same transaction.</summary>
public sealed record InstitutionLinked(Guid CasePublicId, Guid InstitutionPublicId, string Mode) : IEvent;
