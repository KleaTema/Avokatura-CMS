using Ash.Infrastructure.Jobs;
using Ash.Modules.Audit.Application.Digest;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Audit.Endpoints;
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

		// task 041: the daily digest chain and its check
		services.AddSingleton<AuditDigestConnection>();
		services.AddScoped<VerifyAuditChain>();
		services.AddScheduledJob<AuditDigestJob>();
		return services;
	}

	public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapAuditAdmin();
	}
}
