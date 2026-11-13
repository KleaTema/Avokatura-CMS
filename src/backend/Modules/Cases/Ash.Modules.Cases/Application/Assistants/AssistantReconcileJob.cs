using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Cases.Application.Assistants;

/// <summary>
/// "assistant-reconcile" (task 053e), 01:45 Tirane: every case that has an assistant link row or a linked primary is
/// brought in line by <see cref="AssistantSync.SyncCaseAsync"/> — one transaction per case, so one bad case never
/// blocks the others. It repairs what an event missed (e.g. a primary changed outside the API). Idempotent.
/// </summary>
internal sealed class AssistantReconcileJob(IServiceScopeFactory scopes, ILogger<AssistantReconcileJob> logger) : IScheduledJob
{
    public string Name => "assistant-reconcile";

    public JobSchedule Schedule => JobSchedule.DailyAtTirane(1, 45);

    public async Task RunAsync(DateTime nowUtc, CancellationToken ct)
    {
        List<ulong> caseIds;
        await using (var scope = scopes.CreateAsyncScope())
        {
            caseIds = await scope.ServiceProvider.GetRequiredService<AssistantSync>().CasesToReconcileAsync(ct);
        }

        var fixedRows = 0;
        foreach (var caseId in caseIds)
        {
            await using var scope = scopes.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await unitOfWork.BeginAsync(ct);
            try
            {
                fixedRows += await scope.ServiceProvider.GetRequiredService<AssistantSync>().SyncCaseAsync(caseId, ct);
                await unitOfWork.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                logger.LogError(ex, "Assistant reconcile failed for case {CaseId}; the next run tries again.", caseId);
            }
        }

        if (fixedRows > 0)
        {
            logger.LogWarning("Assistant reconcile corrected {Rows} assistant rows on {Cases} cases.", fixedRows, caseIds.Count);
        }
    }
}
