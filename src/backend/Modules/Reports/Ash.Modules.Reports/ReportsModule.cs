using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Reports;

public static class ReportsModule
{
	public static IServiceCollection AddReportsModule(this IServiceCollection services, IConfiguration configuration)
	{
		return services;
	}

	public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints;
	}
}
