using System.Net;
using System.Text.Json;
using Ash.Tests.Integration.Infrastructure;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// The OpenAPI document builds (the frontend generates its client from it with npm run gen:api): a broken schema
/// (e.g. two types with one name) fails here instead of on a developer's machine.
/// </summary>
public class OpenApiTests(MySqlFixture db) : IntegrationTest(db)
{
    [Fact]
    public async Task The_openapi_document_builds_and_lists_the_decision_and_archive_endpoints()
    {
        await using var factory = new AshApiFactory(Db);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var paths = JsonDocument.Parse(text).RootElement.GetProperty("paths");
        paths.TryGetProperty("/api/cases/{id}/decision", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/dossiers/{dossierPublicId}/archive", out _).ShouldBeTrue();
    }
}
