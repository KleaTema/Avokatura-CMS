using Ash.AspNetCore.Authorization;
using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.SharedKernel.Time;

namespace Ash.Api.Jobs;

public sealed record JobRunResponse(string Job, string Outcome, string? Error);

/// <summary>
/// DEVELOPMENT ONLY (mapped only there): POST /api/admin/jobs/{name}/run runs a background job now, through the same
/// runner as the scheduler (named lock, ash_ops.job_runs) — for manual tests (docs/manual-tests.md, task 041).
/// Administrators only (admin.users is an ADMIN permission).
/// </summary>
public static class JobEndpoints
{
    public const string Permission = "admin.users";

    public static IEndpointRouteBuilder MapJobRunEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/jobs/{name}/run",
                async (string name, JobRunner runner, IServiceScopeFactory scopes, PersistenceOptions persistence, IClock clock, CancellationToken ct) =>
                {
                    await using (var scope = scopes.CreateAsyncScope())
                    {
                        if (!scope.ServiceProvider.GetServices<IScheduledJob>().Any(j => j.Name == name))
                        {
                            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Nuk ka punë me emrin \"{name}\".");
                        }
                    }

                    var outcome = await runner.RunAsync(name, clock.UtcNow, ct);
                    string? error = null;
                    if (outcome == JobOutcome.Failed)
                    {
                        var runs = await JobRunStore.ReadAllAsync(persistence.ConnectionString, ct);
                        error = runs.TryGetValue(name, out var run) ? run.LastError : null;
                    }

                    return Results.Ok(new JobRunResponse(name, outcome.ToString(), error));
                })
            .RequirePermission(Permission)
            .Produces<JobRunResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("RunJob");
        return endpoints;
    }
}
