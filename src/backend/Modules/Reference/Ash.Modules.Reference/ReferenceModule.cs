using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Reference;

public static class ReferenceModule
{
	public static IServiceCollection AddReferenceModule(this IServiceCollection services, IConfiguration configuration)
	{
		return services;
	}

	public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints;
	}
}
