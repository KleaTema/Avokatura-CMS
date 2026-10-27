using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Cases;

public static class CasesModule
{
	public static IServiceCollection AddCasesModule(this IServiceCollection services, IConfiguration configuration)
	{
		return services;
	}

	public static IEndpointRouteBuilder MapCasesEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints;
	}
}
