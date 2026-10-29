using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Endpoints;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Cases.Infrastructure.Access;
using Ash.Modules.Cases.Infrastructure.History;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Cases;

public static class CasesModule
{
	public static IServiceCollection AddCasesModule(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddScoped<CasesDbContext>();
		services.AddScoped<CaseAccessService>();
		services.AddScoped<ICaseAssignmentCounts, CaseAssignmentCounts>();
		services.AddScoped<IHistoryWriter, HistoryWriter>();
		services.AddScoped<ICaseAccessService>(sp => sp.GetRequiredService<CaseAccessService>());
		services.AddScoped<RegisterCaseHandler>();
		return services;
	}

	public static IEndpointRouteBuilder MapCasesEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapCaseRegistration();
	}
}
