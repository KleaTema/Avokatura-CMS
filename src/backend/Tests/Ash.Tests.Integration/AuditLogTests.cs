using System.Collections.Concurrent;
using System.Net;
using Ash.Api.Auth;
using Ash.Api.Middleware;
using Ash.Modules.Audit.Contracts;
using Ash.Modules.Audit.Infrastructure;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Runs against the test container (users + ash_audit_local account from the local scripts).
/// Rows are always filtered by correlation id.
/// </summary>
public class AuditLogTests(MySqlFixture db) : IntegrationTest(db)
{
    private const string CaseViewPath = "/api/test/audit/case-view/";
    private const string ForbiddenPath = "/api/test/audit/forbidden";
    private const string EndpointBuilderKey = "__EndpointRouteBuilder";
    private const string TestIp = "203.0.113.7";

    private string AppConnection => Db.AppConnectionString;

    private string AuditConnection => Db.AuditConnectionString;

    private AshApiFactory CreateFactory(string auditConnection, ILogger<AuditWriter>? writerLogger = null) =>
        new(Db, auditConnection: auditConnection, configureServices: services =>
        {
            services.AddSingleton<IStartupFilter, AuditTestStartupFilter>();
            if (writerLogger is not null)
            {
                services.AddSingleton(writerLogger);
            }
        });

    private static HttpRequestMessage Get(string path, string? devUser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (devUser is not null)
        {
            request.Headers.Add("X-Dev-User", devUser);
        }

        return request;
    }

    private static Guid CorrelationOf(HttpResponseMessage response) =>
        Guid.Parse(response.Headers.GetValues(AuditMiddleware.CorrelationHeader).Single());

    [Fact]
    public async Task Request_writes_row_with_user_case_ip_and_correlation_id()
    {
        var caseId = Random.Shared.NextInt64(1_000_000_000_000, 2_000_000_000_000);
        await using var factory = CreateFactory(AuditConnection);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get(CaseViewPath + caseId, "admin@ash.local"), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var correlationId = CorrelationOf(response);

        var rows = await WaitForRowsAsync(correlationId, AuditActions.CaseView);

        var row = rows.ShouldHaveSingleItem();
        row.Outcome.ShouldBe(AuditOutcomes.Success);
        row.CaseId.ShouldBe(caseId);
        row.Ip.ShouldBe(TestIp);
        row.UserId.ShouldBe(await QueryAppScalarAsync("SELECT id FROM ash_core.users WHERE email = 'admin@ash.local'"));
    }

    [Fact]
    public async Task Unauthorized_and_forbidden_requests_write_access_denied()
    {
        await using var factory = CreateFactory(AuditConnection);
        using var client = factory.CreateClient();

        var unauthorized = await client.SendAsync(Get(ForbiddenPath, null), TestContext.Current.CancellationToken);
        var forbidden = await client.SendAsync(Get(ForbiddenPath, "monitor@ash.local"), TestContext.Current.CancellationToken);

        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Only this request's ACCESS_DENIED rows: the same request may also write LOGIN when the user has not logged in recently.
        foreach (var response in new[] { unauthorized, forbidden })
        {
            var rows = await WaitForRowsAsync(CorrelationOf(response), AuditActions.AccessDenied);
            rows.ShouldHaveSingleItem().Outcome.ShouldBe(AuditOutcomes.Denied);
        }
    }

    [Fact]
    public async Task Unreachable_audit_database_does_not_break_the_request_and_logs_an_error()
    {
        var logger = new CapturingLogger();
        await using var factory = CreateFactory("Server=localhost;Port=1;User=x;Password=y;", logger);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Get(CaseViewPath + 1, "admin@ash.local"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!logger.Errors.Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        logger.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Entries_recorded_right_before_host_stops_are_flushed()
    {
        var factory = CreateFactory(AuditConnection);
        Guid correlationId;
        using (var client = factory.CreateClient())
        {
            var response = await client.SendAsync(Get(CaseViewPath + 42, "admin@ash.local"), TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            correlationId = CorrelationOf(response);
        }

        await factory.DisposeAsync();

        var rows = await WaitForRowsAsync(correlationId);
        rows.ShouldHaveSingleItem().Action.ShouldBe(AuditActions.CaseView);
    }

    [Fact]
    public async Task Audit_local_account_cannot_update_or_delete()
    {
        var correlationId = Guid.NewGuid();
        await using var connection = new MySqlConnection(AuditConnection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using (var insert = new MySqlCommand(
            "INSERT INTO ash_audit.audit_log (action, correlation_id) VALUES ('CASE_VIEW', @c)", connection))
        {
            insert.Parameters.AddWithValue("@c", PublicId.ToBytes(correlationId));
            await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        foreach (var sql in new[]
        {
            "UPDATE ash_audit.audit_log SET outcome = 'ERROR' WHERE correlation_id = @c",
            "DELETE FROM ash_audit.audit_log WHERE correlation_id = @c",
        })
        {
            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@c", PublicId.ToBytes(correlationId));
            var ex = await Should.ThrowAsync<MySqlException>(
                () => command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            ex.Message.ShouldContain("command denied");
        }
    }

    private sealed record AuditRow(string Action, string Outcome, long? UserId, long? CaseId, string? Ip);

    private async Task<List<AuditRow>> WaitForRowsAsync(Guid correlationId, string? action = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var rows = (await ReadRowsAsync(correlationId)).Where(x => action is null || x.Action == action).ToList();
            if (rows.Count > 0 || DateTime.UtcNow > deadline)
            {
                return rows;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }

    private async Task<List<AuditRow>> ReadRowsAsync(Guid correlationId)
    {
        await using var connection = new MySqlConnection(AuditConnection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new MySqlCommand(
            "SELECT action, outcome, user_id, case_id, INET6_NTOA(ip_address) FROM ash_audit.audit_log WHERE correlation_id = @c",
            connection);
        command.Parameters.AddWithValue("@c", PublicId.ToBytes(correlationId));
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<AuditRow>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : Convert.ToInt64(reader.GetValue(2)),
                reader.IsDBNull(3) ? null : Convert.ToInt64(reader.GetValue(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    private async Task<long> QueryAppScalarAsync(string sql)
    {
        await using var connection = new MySqlConnection(AppConnection);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private sealed class CapturingLogger : ILogger<AuditWriter>
    {
        private readonly ConcurrentQueue<string> _errors = new();

        public IEnumerable<string> Errors => _errors;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                _errors.Enqueue(formatter(state, exception));
            }
        }
    }

    /// <summary>Test-only endpoints and a fake remote IP, mapped into the host's real pipeline.</summary>
    private sealed class AuditTestStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(TestIp);
                await nextMiddleware();
            });
            app.UseRouting();
            var routes = (IEndpointRouteBuilder)app.Properties[EndpointBuilderKey]!;
            routes.MapGet(CaseViewPath + "{id:long}", (long id, IAuditLog audit) =>
            {
                audit.Record(AuditActions.CaseView, "CASE", id, id);
                return Results.Ok();
            }).RequireAuthorization();
            routes.MapGet(ForbiddenPath, () => Results.Ok()).RequirePermission("test.never_granted");
            next(app);
        };
    }
}
