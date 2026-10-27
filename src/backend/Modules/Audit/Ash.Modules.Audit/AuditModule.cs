using Ash.Modules.Audit.Contracts;
using Ash.Modules.Audit.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Audit;

public static class AuditModule
{
	public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<AuditWriter>();
		services.AddHostedService(sp => sp.GetRequiredService<AuditWriter>());
		services.AddScoped<AuditRequestContext>();
		services.AddScoped<IAuditLog, AuditLog>();
		return services;
	}

	public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)	{
		return endpoints;
	}
}
