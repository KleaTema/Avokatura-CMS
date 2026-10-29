using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>An institution's link to a case ended (its last party was removed). Same transaction.</summary>
public sealed record InstitutionUnlinked(Guid CasePublicId, Guid InstitutionPublicId, string Mode) : IEvent;
