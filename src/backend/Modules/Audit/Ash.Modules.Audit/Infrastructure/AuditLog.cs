using System.Text.Json;
using Ash.Modules.Audit.Contracts;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Audit.Infrastructure;

public sealed class AuditLog(AuditWriter writer, AuditRequestContext context, IClock clock) : IAuditLog
{
    public void Record(
        string action,
        string? entityType = null,
        long? entityId = null,
        long? caseId = null,
        object? details = null,
        string outcome = AuditOutcomes.Success,
        AuditActor? actor = null)
    {
        actor ??= context.Actor;
        var detailsJson = details is null ? null : JsonSerializer.Serialize(details);

        writer.Enqueue(new AuditEntry(
            clock.UtcNow, actor?.UserId, actor?.Display, actor?.Roles, action, outcome,
            entityType, entityId, caseId, context.IpAddress, context.UserAgent, context.CorrelationId, detailsJson));
    }
}
