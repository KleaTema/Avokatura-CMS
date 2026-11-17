using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ash.Modules.Cases.Contracts;
using Ash.SharedKernel.Ids;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Task 053k: docs/permissions-matrix.md can never drift. Every column of its table is one real case and user; every
/// cell is compared with the API's effective permissions (GET /api/cases/{id} permissions[], none for a 404) and, for
/// the row "chat", with the case chat's participants. A permission the API gives without a row in the table fails.
/// </summary>
public partial class PermissionsMatrixTests(MySqlFixture db) : IntegrationTest(db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string ChatRow = "chat";
    private const string Ministry = "Ministria e Financave dhe Ekonomisë";

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private sealed record Matrix(IReadOnlyList<string> Columns, IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> Rows);

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    /// <summary>The table between the matrix markers: column keys from the headers, one row per permission code.</summary>
    private static Matrix ReadMatrix()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "permissions-matrix.md")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("docs/permissions-matrix.md not found above the test output");
        var text = File.ReadAllText(Path.Combine(dir.FullName, "docs", "permissions-matrix.md"));
        var table = text.Split("<!-- matrix:begin -->")[1].Split("<!-- matrix:end -->")[0]
            .Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('|')).ToList();
        static string[] Cells(string line) => line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();

        var columns = Cells(table[0]).Skip(1).Select(h => Code().Match(h).Groups[1].Value).ToList();
        var rows = new Dictionary<string, IReadOnlyDictionary<string, bool>>();
        foreach (var line in table.Skip(2))
        {
            var cells = Cells(line);
            var code = Code().Match(cells[0]).Groups[1].Value;
            cells.Length.ShouldBe(columns.Count + 1, code);
            rows[code] = columns.Select((c, i) => (c, cells[i + 1] switch
            {
                "●" => true,
                "–" => false,
                var other => throw new InvalidOperationException($"{code}/{c}: a cell is ● or –, not \"{other}\""),
            })).ToDictionary(x => x.c, x => x.Item2);
        }

        return new Matrix(columns, rows);
    }

    [Fact]
    public async Task The_documented_matrix_is_what_the_API_gives_every_role()
    {
        var matrix = ReadMatrix();
        await using var seed = await CaseSeed.CreateAsync(Db.AppConnectionString);
        // the ACTIVE case: avokat1 primary, avokat2 secondary, ndihmes assistant, the ministry MONITORING with its jurist
        var monitored = seed.ActiveCaseId;
        // a case DELEGATED to the ministry, with a jurist row that must give nothing (task 053k)
        var (delegated, _) = await seed.AddOfficeCaseAsync(seed.OfficeTrId, "mx", seed.User("avokat1").Id);
        await Scalar($"""
            INSERT INTO case_institutions (case_id, institution_id, link_mode, started_by)
            SELECT {delegated}, i.id, 'DELEGATED', (SELECT id FROM users WHERE email = 'kryetar@ash.local')
              FROM institutions i WHERE i.name = '{Ministry}';
            UPDATE cases SET category_id = (SELECT id FROM case_categories WHERE code = 'DELEGIM') WHERE id = {delegated};
            INSERT INTO case_assignments (case_id, user_id, case_role, assigned_by)
            SELECT {delegated}, (SELECT id FROM users WHERE email = 'jurist@ash.local'), 'INSTITUTION_JURIST',
                   (SELECT id FROM users WHERE email = 'institucion@ash.local');
            """);

        var columns = new Dictionary<string, (string Email, long CaseId)>
        {
            ["primary"] = ("avokat1@ash.local", monitored),
            ["secondary"] = ("avokat2@ash.local", monitored),
            ["assistant"] = ("ndihmes@ash.local", monitored),
            ["head"] = ("kryetar@ash.local", monitored),
            ["monitor"] = ("monitor@ash.local", monitored),
            ["protokoll"] = ("protokoll@ash.local", seed.RegisteredCaseId),
            ["admin"] = ("admin@ash.local", monitored),
            ["inst-mon"] = ("institucion@ash.local", monitored),
            ["inst-del"] = ("institucion@ash.local", delegated),
            ["jurist-mon"] = ("jurist@ash.local", monitored),
            ["jurist-del"] = ("jurist@ash.local", delegated),
            ["deleg-del"] = ("delegim@ash.local", delegated),
            ["deleg-mon"] = ("delegim@ash.local", monitored),
        };
        matrix.Columns.Order().ShouldBe(columns.Keys.Order(), "the matrix columns and the test's scenarios");

        await using var factory = new AshApiFactory(Db);
        var failures = new List<string>();
        foreach (var (key, (email, caseId)) in columns)
        {
            var publicId = PublicId.FromBytes((byte[])(await Scalar($"SELECT public_id FROM cases WHERE id = {caseId}"))!);
            using var client = factory.CreateClient().AsUser(email);
            var response = await client.GetAsync($"/api/cases/{publicId}", Ct);
            var actual = new HashSet<string>();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var detail = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
                actual.UnionWith(detail.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!));
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{key}: {email}");
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var participants = await scope.ServiceProvider.GetRequiredService<ICaseLookup>().GetChatParticipantIdsAsync(caseId, Ct);
                var userId = Convert.ToInt64(await Scalar($"SELECT id FROM users WHERE email = '{email}'"));
                if (participants.Contains(userId))
                {
                    actual.Add(ChatRow);
                }
            }

            foreach (var code in actual.Where(c => !matrix.Rows.ContainsKey(c)))
            {
                failures.Add($"{key}: the API gives \"{code}\", which has no row in the matrix");
            }

            foreach (var (code, cells) in matrix.Rows)
            {
                if (cells[key] != actual.Contains(code))
                {
                    failures.Add($"{key} ({email}) / {code}: matrix {(cells[key] ? "●" : "–")}, API {(actual.Contains(code) ? "●" : "–")}");
                }
            }
        }

        failures.ShouldBeEmpty(string.Join('\n', failures));
    }
}
