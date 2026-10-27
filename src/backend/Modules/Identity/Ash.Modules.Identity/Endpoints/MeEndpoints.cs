using Ash.Modules.Identity.Application.GetMe;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Identity.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/identity/me", async (GetMeHandler handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(ct);
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : Results.Problem(
                        title: result.Error!.Message,
                        statusCode: StatusCodes.Status404NotFound,
                        extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
            })
            .RequireAuthorization()
            .WithName("GetMe")
            .Produces<GetMeResponse>()
            .ProducesProblem(404);
        return endpoints;
    }
}
