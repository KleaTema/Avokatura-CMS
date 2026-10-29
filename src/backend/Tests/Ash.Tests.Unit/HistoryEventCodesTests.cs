using System.Text.RegularExpressions;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure.History;
using Shouldly;

namespace Ash.Tests.Unit;

public class HistoryEventCodesTests
{
    /// <summary>
    /// The enum is the list of record. Backward compatibility: every code 004 lists still exists (codes already
    /// stored are never removed or renamed). New codes may be added in the code without touching the SQL.
    /// </summary>
    [Fact]
    public void Every_code_listed_in_the_sql_still_exists_and_codes_are_distinct()
    {
        var sqlCodes = ReadCodesFromSql();
        var mapped = Enum.GetValues<HistoryEventType>().Select(HistoryEventCodes.ToCode).ToList();

        sqlCodes.ShouldBeSubsetOf(mapped);
        mapped.Distinct().Count().ShouldBe(mapped.Count);
    }

    [Fact]
    public void Every_code_fits_the_event_type_column()
    {
        // case_history_events.event_type is VARCHAR(40)
        Enum.GetValues<HistoryEventType>().Select(HistoryEventCodes.ToCode).ShouldAllBe(code => code.Length <= 40);
    }

    [Fact]
    public void Unknown_enum_value_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => HistoryEventCodes.ToCode((HistoryEventType)999));
    }

    private static List<string> ReadCodesFromSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "Database", "Ash.Database", "Scripts", "004_core_documents_history.sql");
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                var start = text.IndexOf("event_type values used by the API", StringComparison.Ordinal);
                var end = text.IndexOf("CREATE TABLE case_history_events", StringComparison.Ordinal);
                return Regex.Matches(text[start..end], "[A-Z]+(?:_[A-Z]+)*")
                    .Select(m => m.Value)
                    .Where(v => v.Contains('_') || v is "DELEGATED")
                    .Where(v => v != "API")
                    .Distinct()
                    .ToList();
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("004_core_documents_history.sql not found above the test output directory.");
    }
}
