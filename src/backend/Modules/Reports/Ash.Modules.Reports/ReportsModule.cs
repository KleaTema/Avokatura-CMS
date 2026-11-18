using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Persistence;
using Ash.Modules.Reports.Application.Statistics;
using Ash.Modules.Reports.Application;
using Ash.Modules.Reports.Application.ActiveCases;
using Ash.Modules.Reports.Application.ClosedCases;
using Ash.Modules.Reports.Endpoints;
using Ash.Modules.Reports.Export;
using Ash.Modules.Reports.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ash.Modules.Reports;

public static class ReportsModule
{
	public static IServiceCollection AddReportsModule(this IServiceCollection services, IConfiguration configuration)
	{
		// tasks 042/043: ConnectionStrings:Reporting (account ash_reporting, the read replica) — the App connection locally
		var reporting = configuration.GetConnectionString("Reporting");
		var exportSeconds = configuration.GetValue<double?>("Reports:ExportQueryTimeLimitSeconds");
		var maxRows = configuration.GetValue<int?>("Reports:MaxExportRows");
		services.AddSingleton(sp =>
		{
			var persistence = sp.GetRequiredService<PersistenceOptions>();
			return new ReportingOptions(
				string.IsNullOrWhiteSpace(reporting) ? persistence.ConnectionString : reporting,
				persistence.QueryTimeLimit,
				exportSeconds is { } s ? TimeSpan.FromSeconds(s) : ReportingOptions.DefaultExportTimeLimit,
				maxRows ?? ReportingOptions.DefaultMaxExportRows);
		});
		services.AddSingleton<ReportingDb>();
		services.AddScoped<ReportAudienceResolver>(); // task 053h: who sees what, reports and statistics
		services.AddScoped<CaseLinks>();
		services.AddScoped<Application.Builder.CaseReportBuilder>();
		// task 053j: the same registry for the Protokoll's "Vazhdo çështje ekzistuese" ("+ Shto filtër")
		services.AddScoped<Contracts.ICaseFilterCatalog, Application.Builder.CaseFilterCatalog>();
		services.AddScoped<Application.Builder.StatisticsBuilder>();
		services.AddScoped<Application.Builder.SavedReports>();
		services.AddScoped<Ash.SharedKernel.Filters.ITableFilterSource, Application.Builder.SavedReportFilterSource>(); // task 053m
		services.AddScoped<ReportScope>();
		services.AddScoped<ActiveCasesReport>();
		services.AddScoped<ClosedCasesReport>();
		services.AddScoped<ReportExports>();
		// task 044: the statistics
		services.AddMemoryCache();
		services.AddScoped<StatisticsScope>();
		services.AddScoped<StatisticsQueries>();
		services.AddScoped<StatisticsExport>();
		services.AddScoped<StatisticsSnapshots>();
		services.AddScheduledJob<StatisticsSnapshotJob>();
		return services;
	}

	public static IEndpointRouteBuilder MapReportsEndpoints(this IEndpointRouteBuilder endpoints)
	{
		return endpoints.MapReports().MapReportBuilder(); // task 053h: the report builder
	}
}
