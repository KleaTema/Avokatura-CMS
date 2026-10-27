namespace Ash.SharedKernel.Identity;

public interface ICurrentUser
{
    long UserId { get; }
    Guid PublicId { get; }
    long? OfficeId { get; }
    long? InstitutionId { get; }
    IReadOnlyCollection<string> Roles { get; }
    bool HasPermission(string permission);
}
