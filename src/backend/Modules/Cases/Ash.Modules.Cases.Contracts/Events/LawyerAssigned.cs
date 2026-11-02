using Ash.SharedKernel.Events;

namespace Ash.Modules.Cases.Contracts.Events;

/// <summary>
/// The Kryetar assigned the primary (and optionally the secondary) lawyer; the case became ACTIVE. Recipients: the
/// lawyers and the users of the institutions linked to the case. Handlers run in the same transaction.
/// TODO(task-040): Notifications tell the primary and secondary lawyer and the linked institutions.
/// </summary>
public sealed record LawyerAssigned(
    Guid CasePublicId,
    Guid PrimaryUserPublicId,
    Guid? SecondaryUserPublicId,
    IReadOnlyList<Guid> LinkedInstitutionPublicIds) : IEvent;
