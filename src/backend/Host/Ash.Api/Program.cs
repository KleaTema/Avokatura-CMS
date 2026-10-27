using Ash.Api.Errors;
using Ash.Api.Health;
using Ash.Api.Persistence;
using Ash.Infrastructure;
using Ash.Modules.Audit;
using Ash.Modules.Cases;
using Ash.Modules.Chat;
using Ash.Modules.Documents;
using Ash.Modules.Identity;
using Ash.Modules.Notifications;
using Ash.Modules.Reference;
using Ash.Modules.Reports;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/ash-.log", rollingInterval: RollingInterval.Day));

var connectionString = builder.Configuration.GetConnectionString("App")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:App is missing. Set it with: dotnet user-secrets set \"ConnectionStrings:App\" \"<value>\"");

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddAshInfrastructure(connectionString);
builder.Services.AddHealthChecks().AddCheck<MySqlHealthCheck>("mysql");

builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddReferenceModule(builder.Configuration);
builder.Services.AddCasesModule(builder.Configuration);
builder.Services.AddDocumentsModule(builder.Configuration);
builder.Services.AddNotificationsModule(builder.Configuration);
builder.Services.AddAuditModule(builder.Configuration);
builder.Services.AddReportsModule(builder.Configuration);
builder.Services.AddChatModule(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<UnitOfWorkMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/health");

app.MapIdentityEndpoints();
app.MapReferenceEndpoints();
app.MapCasesEndpoints();
app.MapDocumentsEndpoints();
app.MapNotificationsEndpoints();
app.MapAuditEndpoints();
app.MapReportsEndpoints();
app.MapChatEndpoints();

app.Run();
