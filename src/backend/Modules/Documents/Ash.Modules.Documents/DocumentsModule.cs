using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Documents;

public static class DocumentsModule
{
	public static IServiceCollection AddDocumentsModule(this IServiceCollection services, IConfiguration configuration)
	{
		return services;
	}

	public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints;
	}
}
