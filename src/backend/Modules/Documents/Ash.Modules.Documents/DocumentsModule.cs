using Ash.Modules.Documents.Application;
using Ash.Modules.Documents.Contracts;
using Ash.Modules.Documents.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Documents;

public static class DocumentsModule
{
	public static IServiceCollection AddDocumentsModule(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddScoped<DocumentsDbContext>();
		services.AddScoped<IDocumentCounts, DocumentCounts>();
		return services;
	}

	public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints;
	}
}
