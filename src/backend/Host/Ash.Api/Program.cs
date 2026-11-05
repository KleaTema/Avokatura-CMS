using Ash.Api.Auth;
using Ash.Api.Errors;
using Ash.Api.Health;
using Ash.Api.Middleware;
using Ash.Api.Persistence;
using Ash.Api.Jobs;
using Ash.Infrastructure;
using Ash.Infrastructure.Jobs;
using Ash.Infrastructure.Scanning;
using Ash.Infrastructure.Storage;
using Ash.Modules.Audit;
using Ash.Modules.Cases;
using Ash.Modules.Chat;
using Ash.Modules.Documents;
using Ash.Modules.Identity;
using Ash.SharedKernel.Time;
using Ash.Modules.Notifications;
using Ash.Modules.Reference;
using Ash.Modules.Reports;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using Ash.Api.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    // sinks and enrichers registered in DI too (the integration tests capture events this way)
    .ReadFrom.Services(services)
    // task 040: the host's per-request lines carry the full query string (the hubs' access_token / dev_user);
    // UseSerilogRequestLogging below logs each request without it
    .MinimumLevel.Override(LogRedaction.HostingDiagnostics, LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.With<LogRedaction>()
    .WriteTo.Console()
    .WriteTo.File("logs/ash-.log", rollingInterval: RollingInterval.Day));

var connectionString = builder.Configuration.GetConnectionString("App")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:App is missing. Set it with: dotnet user-secrets set \"ConnectionStrings:App\" \"<value>\"");

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
// read queries are stopped by MySQL after Database:QueryTimeLimitSeconds (default 5) → 503, never a hang (task 046)
builder.Services.AddAshInfrastructure(connectionString,
    builder.Configuration.GetValue<double?>("Database:QueryTimeLimitSeconds") is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
// object storage (task 032); keys from user-secrets / environment, never logged (StorageOptions.ToString hides them)
builder.Services.AddAshStorage(builder.Configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions());
// virus scanning (task 033): NoOp only in Development — elsewhere the app refuses to start with it
builder.Services.AddAshScanning(
    builder.Configuration.GetSection(ScannerOptions.Section).Get<ScannerOptions>() ?? new ScannerOptions(),
    builder.Environment.IsDevelopment());
// background jobs (task 041): the in-house scheduler; Jobs:Enabled = false runs nothing on its own
builder.Services.AddAshJobs(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<MySqlHealthCheck>("mysql")
    .AddCheck<StorageHealthCheck>("storage")
    .AddCheck<JobsHealthCheck>("jobs");
builder.Services.TryAddSingleton<IClock, SystemClock>();
builder.Services.AddAshAuthentication(builder.Configuration, builder.Environment);

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddReferenceModule(builder.Configuration);
builder.Services.AddCasesModule(builder.Configuration);
builder.Services.AddDocumentsModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddReportsModule(builder.Configuration);
builder.Services.AddChatModule(builder.Configuration);

var app = builder.Build();

// RequestPath only — never the query string (task 040: hub credentials travel there)
app.UseSerilogRequestLogging(o => o.MessageTemplate =
    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms");
app.UseMiddleware<AuditMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<UnitOfWorkMiddleware>();
app.UseAuthentication();
// fills ICurrentUser before permission policies are evaluated
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
    // task 041: run a background job by hand (manual tests); never mapped outside Development
    app.MapJobRunEndpoint();
}

app.MapHealthChecks("/health").AllowAnonymous();

app.MapIdentityEndpoints();
app.MapReferenceEndpoints();
app.MapCasesEndpoints();
app.MapDocumentsEndpoints();
app.MapNotificationsEndpoints();
app.MapAuditEndpoints();
app.MapReportsEndpoints();
app.MapChatEndpoints();

app.Run();

public partial class Program;
