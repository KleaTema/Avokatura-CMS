using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Audit;

public static class AuditModule
{
	public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
	{
		return services;
	}

	public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints;
	}
}
