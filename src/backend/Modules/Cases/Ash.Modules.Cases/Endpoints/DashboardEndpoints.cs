using Ash.Modules.Cases.Application.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Cases.Endpoints;

/// <summary>Task 046: the caller's dashboard. Any authenticated user; the widgets follow the caller's tabs and permissions.</summary>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard",
                async (DashboardHandler handler, CancellationToken ct) => Results.Ok(await handler.Handle(ct)))
            .RequireAuthorization()
            .Produces<DashboardResponse>()
            .WithName("GetDashboard");
        return endpoints;
    }
}
