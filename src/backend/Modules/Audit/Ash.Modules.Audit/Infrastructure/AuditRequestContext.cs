using Ash.Modules.Audit.Contracts;

namespace Ash.Modules.Audit.Infrastructure;

/// <summary>Per-request data stamped on audit entries; filled by the API's AuditMiddleware and CurrentUserMiddleware.</summary>
public sealed class AuditRequestContext
{
    public Guid? CorrelationId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public AuditActor? Actor { get; set; }
}
