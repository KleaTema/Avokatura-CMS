using Ash.Api.Auth;
using Ash.Api.Errors;
using Ash.Api.Health;
using Ash.Api.Middleware;
using Ash.Api.Persistence;
using Ash.Api.Security;
using Ash.Api.Configuration;
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

// task 052: Europe/Tirane and sq-AL must exist (a container without ICU / tzdata would compute wrong days)
RuntimeSelfCheck.Assert();

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
    // task 052: every line names the environment and the release; the correlation id comes per request
    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
    .Enrich.WithProperty("Version", AppVersion.Current(context.Configuration))
    .WriteTo.Logger(sink => ConfigureSinks(sink, context.HostingEnvironment)));

// task 052: Development → readable console + a local file; elsewhere → JSON lines on stdout only (the container's
// root filesystem is read-only; the platform collects stdout)
static void ConfigureSinks(Serilog.LoggerConfiguration sink, IHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        sink.WriteTo.Console().WriteTo.File("logs/ash-.log", rollingInterval: RollingInterval.Day);
    }
    else
    {
        sink.WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter());
    }
}

// task 051: outside Development no development-only feature may be switched on — refuse to start
DevFeatures.AssertSafe(builder.Configuration, builder.Environment);
// task 051: and every database connection encrypted (SslMode=Required or stricter)
DatabaseSecurity.AssertSsl(builder.Configuration, builder.Environment);
// task 052: every required setting present — all missing ones listed at once
Ash.Api.Configuration.StartupValidator.Assert(builder.Configuration, builder.Environment);

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
// task 052: /health/live = the process; /health/ready (and /health) = every dependency below
builder.Services.AddHealthChecks()
    .AddCheck<MySqlHealthCheck>("mysql")
    .AddTypeActivatedCheck<ConnectionHealthCheck>("mysql-audit", args: "Audit")
    .AddTypeActivatedCheck<ConnectionHealthCheck>("mysql-chat", args: "Chat")
    .AddTypeActivatedCheck<ConnectionHealthCheck>("mysql-reporting", args: "Reporting")
    .AddCheck<MySqlSettingsHealthCheck>("mysql-settings")
    .AddCheck<StorageHealthCheck>("storage")
    .AddCheck<ScannerHealthCheck>("scanner")
    .AddCheck<JobsHealthCheck>("jobs");
builder.Services.AddHostedService<MySqlSettingsStartupCheck>();
builder.Services.TryAddSingleton<IClock, SystemClock>();
builder.Services.AddAshAuthentication(builder.Configuration, builder.Environment);
// task 051: CORS only for the configured frontend origin(s) — none when same-origin (production)
builder.Services.AddAshCors(builder.Configuration);
// task 052: X-Forwarded-For / -Proto only from the configured reverse proxies
builder.Services.AddAshForwardedHeaders(builder.Configuration);
// task 051: rate limits per user (anonymous per IP) on the expensive / cross-office endpoints
builder.Services.AddAshRateLimiting(builder.Configuration);
// task 051: body 1 MB (upload and Organika excepted), JSON depth 32, multipart limits
builder.Services.AddAshRequestLimits();
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = RequestLimits.DefaultMaxBodyBytes);
builder.Services.AddHsts(o =>
{
    o.MaxAge = TimeSpan.FromDays(365);
    o.IncludeSubDomains = true;
});

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddReferenceModule(builder.Configuration);
builder.Services.AddCasesModule(builder.Configuration);
builder.Services.AddDocumentsModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddReportsModule(builder.Configuration);
builder.Services.AddChatModule(builder.Configuration);
// task 052: SignalR across several instances (Redis backplane when Ash:Redis:ConnectionString is set)
builder.Services.AddAshSignalRBackplane(builder.Configuration);

var app = builder.Build();

// task 052: first of all — the client's address and scheme from the trusted proxy (audit, rate limits, HSTS)
app.UseForwardedHeaders();
// task 052: the correlation id of every log line of the request (and the X-Request-Id answer header)
app.UseMiddleware<CorrelationIdMiddleware>();

// RequestPath only — never the query string (task 040: hub credentials travel there)
app.UseSerilogRequestLogging(o => o.MessageTemplate =
    "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms");
// task 051: security headers on every response; HSTS only outside Development (and only over HTTPS)
app.UseMiddleware<Ash.Api.Security.SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors(Ash.Api.Security.CorsSetup.PolicyName);
// task 051: the body limit of the matched route (routing runs first: the endpoint is known here)
app.UseMiddleware<RequestLimitsMiddleware>();
app.UseMiddleware<AuditMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<UnitOfWorkMiddleware>();
app.UseAuthentication();
// fills ICurrentUser before permission policies are evaluated
app.UseMiddleware<CurrentUserMiddleware>();
// task 051: after the user is known (limits are per user), before the endpoint runs
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

// task 041: run a background job by hand (manual tests); never mapped outside Development (task 051: DevFeatures)
if (DevFeatures.JobRunEndpointEnabled(app.Configuration, app.Environment))
{
    app.MapJobRunEndpoint();
}

app.MapHealthChecks("/health").AllowAnonymous();
// task 052: liveness (the process, no dependency — a restart would not fix a database) and readiness (send traffic?)
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapVersion();
// task 051: how the login page signs in (anonymous: read before the login)
app.MapAuthConfig(DevFeatures.HeaderAuthEnabled(app.Configuration, app.Environment));

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
