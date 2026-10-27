using Ash.SharedKernel.Results;

namespace Ash.Modules.Cases.Contracts;

/// <summary>The single gate to case data: every case read or write is checked here.</summary>
public interface ICaseAccessService
{
    Task<bool> CanViewAsync(long caseId, CancellationToken cancellationToken = default);

    Task<bool> HasAsync(long caseId, string permissionCode, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="CaseAccessException"/>: NotFound when the user cannot view the case, Forbidden when they lack the permission.</summary>
    Task RequireAsync(long caseId, string permissionCode, CancellationToken cancellationToken = default);
}

public sealed class CaseAccessException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}
