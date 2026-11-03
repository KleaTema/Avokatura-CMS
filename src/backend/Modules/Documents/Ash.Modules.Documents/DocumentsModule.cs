using Ash.Modules.Documents.Application;
using Ash.Modules.Documents.Application.Upload;
using Ash.Modules.Documents.Contracts;
using Ash.Modules.Documents.Endpoints;
using Ash.Modules.Documents.Infrastructure;
using Ash.Modules.Documents.Infrastructure.Jobs;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Documents;

public static class DocumentsModule
{
	/// <remarks>Needs IFileStorage (AddAshStorage) and IVirusScanner (AddAshScanning) from the host.</remarks>
	public static IServiceCollection AddDocumentsModule(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddScoped<DocumentsDbContext>();
		services.AddScoped<IDocumentCounts, DocumentCounts>();
		services.AddScoped<UploadDocumentHandler>();
		services.AddSingleton<ScanQueueJob>();
		services.AddHostedService(sp => sp.GetRequiredService<ScanQueueJob>());
		return services;
	}

	public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapDocuments();
	}
}
