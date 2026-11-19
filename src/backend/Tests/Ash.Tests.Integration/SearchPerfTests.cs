using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ash.SharedKernel.Time;
using Ash.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Shouldly;

namespace Ash.Tests.Integration;

/// <summary>
/// Live search timings with a realistic seed — OPT-IN (category "Perf", explicit: never in the normal suite). 100,000
/// cases over 20 offices (DashboardSeed), 2 parties each, 3 documents each, one case with 1,000 documents. Measures the
/// 1- and 2-character searches of the case list (an office head: one office's cases; the monitor: every case) and of a
/// case's documents list. Reports the numbers; the 500 ms rule for 1 character is decided by the product owner, so
/// nothing is asserted on time here. Run:
/// <c>dotnet test Tests/Ash.Tests.Integration --filter "FullyQualifiedName~SearchPerfTests" -- xUnit.Explicit=only</c>
/// </summary>
public class SearchPerfTests(MySqlFixture db) : IntegrationTest(db)
{
    private const int Cases = 100_000;
    private const int Runs = 5;

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    [Fact(Explicit = true, Timeout = 1_800_000)]
    [Trait("Category", "Perf")]
    public async Task Search_timings_with_100000_cases()
    {
        var ct = TestContext.Current.CancellationToken;
        var output = TestContext.Current.TestOutputHelper;
        await DashboardSeed.CreateAsync(Db.AppConnectionString, Cases, DashboardTests.Now, offices: 20, spreadHearings: true);
        var (caseId, casePublicId) = await SeedPartiesAndDocumentsAsync(ct);

        await using var factory = new AshApiFactory(Db, configureServices: s => s.AddSingleton<IClock>(new FixedClock(DashboardTests.Now)));
        var lines = new List<string>();
        foreach (var (user, label) in new[] { ("kryetar@ash.local", "Kryetar (one office)"), ("monitor@ash.local", "Monitor (every case)") })
        {
            using var client = factory.CreateClient().AsUser(user);
            // "" = the list without a search (the baseline the search adds to)
            foreach (var q in new[] { "", "5", "56", "a", "ab", "e", "Objekt", "Hoxha", "2026-12" })
            {
                var query = q == "" ? "status=ALL" : $"q={Uri.EscapeDataString(q)}";
                lines.Add($"cases   {label,-22} q={q,-8} {await TimeAsync(client, $"/api/cases?{query}&limit=10", ct)}");
            }
        }

        using (var kryetar = factory.CreateClient().AsUser("kryetar@ash.local"))
        {
            foreach (var q in new[] { "", "a", "ab", "5", "56" })
            {
                var query = q == "" ? string.Empty : $"q={Uri.EscapeDataString(q)}&";
                lines.Add($"docs    case with 1,000 documents  q={q,-8} {await TimeAsync(kryetar, $"/api/cases/{casePublicId}/documents?{query}limit=10", ct)}");
            }
        }

        output?.WriteLine($"Live search with {Cases:N0} cases ({Runs} runs each after one warm-up; median / max):");
        foreach (var line in lines)
        {
            output?.WriteLine(line);
        }

        caseId.ShouldBeGreaterThan(0UL);
    }

    private static async Task<string> TimeAsync(HttpClient client, string url, CancellationToken ct)
    {
        (await client.GetAsync(url, ct)).StatusCode.ShouldBe(HttpStatusCode.OK, url); // warm-up (plans, caches)
        var times = new List<long>();
        var total = 0;
        for (var i = 0; i < Runs; i++)
        {
            var watch = Stopwatch.StartNew();
            var response = await client.GetAsync(url, ct);
            watch.Stop();
            response.StatusCode.ShouldBe(HttpStatusCode.OK, url);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            total = body.TryGetProperty("total", out var t) ? t.GetInt32() : -1;
            times.Add(watch.ElapsedMilliseconds);
        }

        times.Sort();
        return $"median {times[Runs / 2],5} ms  max {times[^1],5} ms  (total {total:N0})";
    }

    /// <summary>2 parties per seeded case, 3 documents per case, and 1,000 documents on one Tirana case.</summary>
    private async Task<(ulong CaseId, Guid PublicId)> SeedPartiesAndDocumentsAsync(CancellationToken ct)
    {
        await using var connection = new MySqlConnection(Db.AppConnectionString);
        await connection.OpenAsync(ct);
        const string sql = """
            SET SESSION cte_max_recursion_depth = 1000000;
            SET @prot = (SELECT id FROM users WHERE email = 'protokoll@ash.local');
            SET @plaintiff = (SELECT id FROM party_roles WHERE code = 'PADITES');
            SET @defendant = (SELECT id FROM party_roles WHERE code = 'I_PADITUR');
            SET @doctype = (SELECT MIN(id) FROM document_types);
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, identifier, created_by)
            SELECT c.id, @plaintiff, 'NATURAL',
                   CONCAT(ELT(c.id % 8 + 1, 'Arben', 'Blerina', 'Çlirim', 'Dritë', 'Ëndrit', 'Fatjona', 'Gëzim', 'Hëna'), ' ',
                          ELT(c.id % 7 + 1, 'Hoxha', 'Përgjegjësi', 'Shehu', 'Çela', 'Dervishi', 'Krasniqi', 'Mëhilli')), NULL, @prot
              FROM cases c;
            INSERT INTO case_parties (case_id, party_role_id, party_kind, display_name, identifier, created_by)
            SELECT c.id, @defendant, 'LEGAL', CONCAT('Shoqëria ', ELT(c.id % 5 + 1, 'Alfa', 'Beta', 'Gama', 'Delta', 'Omega'), ' sh.p.k.'),
                   CONCAT('L', LPAD(c.id, 8, '0'), 'A'), @prot
              FROM cases c;
            INSERT INTO documents (dossier_id, case_id, document_type_id, title, original_file_name, content_type, size_bytes,
                                   sha256, storage_bucket, storage_key, scan_status, uploaded_by)
            WITH RECURSIVE k (n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM k WHERE n < 3)
            SELECT c.dossier_id, c.id, @doctype, CONCAT(ELT(k.n, 'Padi', 'Përgjigje', 'Vendim'), ' ', c.id), 'a.pdf', 'application/pdf', 1000,
                   UNHEX(SHA2(CONCAT(c.id, '-', k.n), 256)), 'perf', CONCAT('perf/', c.id, '/', k.n), 'CLEAN', @prot
              FROM cases c CROSS JOIN k;
            SET @case = (SELECT MAX(c.id) FROM cases c JOIN offices o ON o.id = c.office_id WHERE o.code = 'TR');
            INSERT INTO documents (dossier_id, case_id, document_type_id, title, original_file_name, content_type, size_bytes,
                                   sha256, storage_bucket, storage_key, scan_status, uploaded_by)
            WITH RECURSIVE k (n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM k WHERE n < 1000)
            SELECT c.dossier_id, c.id, @doctype, CONCAT(ELT(k.n % 4 + 1, 'Padi', 'Përgjigje', 'Ankim', 'Vendim'), ' nr. ', k.n), 'b.pdf',
                   'application/pdf', 1000, UNHEX(SHA2(CONCAT('one-', k.n), 256)), 'perf', CONCAT('perf/one/', k.n), 'CLEAN', @prot
              FROM cases c CROSS JOIN k WHERE c.id = @case;
            SELECT @case AS id, BIN_TO_UUID(public_id, 1) AS public_id FROM cases WHERE id = @case;
            """;
        await using var command = new MySqlCommand(sql, connection) { CommandTimeout = 1200 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        do
        {
            if (reader.FieldCount == 2 && await reader.ReadAsync(ct))
            {
                return (Convert.ToUInt64(reader.GetValue(0)), Guid.Parse(reader.GetString(1)));
            }
        }
        while (await reader.NextResultAsync(ct));

        throw new InvalidOperationException("The seeded case was not found.");
    }
}
