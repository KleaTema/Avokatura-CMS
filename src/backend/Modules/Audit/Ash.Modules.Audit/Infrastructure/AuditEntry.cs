namespace Ash.Modules.Audit.Infrastructure;

public sealed record AuditEntry(
    DateTime OccurredAt,
    long? UserId,
    string? UserDisplay,
    string? RoleCodes,
    string Action,
    string Outcome,
    string? EntityType,
    long? EntityId,
    long? CaseId,
    string? IpAddress,
    string? UserAgent,
    Guid? CorrelationId,
    string? DetailsJson);
