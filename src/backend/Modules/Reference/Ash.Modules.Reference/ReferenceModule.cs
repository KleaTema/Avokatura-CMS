using Ash.Modules.Reference.Application;
using Ash.Modules.Reference.Contracts;
using Ash.Modules.Reference.Endpoints;
using Ash.Modules.Reference.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Reference;

public static class ReferenceModule
{
	public static IServiceCollection AddReferenceModule(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddMemoryCache();
		services.AddSingleton<ReferenceCache>();
		services.AddSingleton<IReferenceCache>(sp => sp.GetRequiredService<ReferenceCache>());
		services.AddScoped<ReferenceDbContext>();
		services.AddScoped<IReferenceData, ReferenceData>();
		services.AddScoped<SearchJudgesHandler>();
		return services;
	}

	public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapReference();
	}
}
