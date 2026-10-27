using System.Text.RegularExpressions;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure.History;
using Shouldly;

namespace Ash.Tests.Unit;

public class HistoryEventCodesTests
{
    [Fact]
    public void Every_enum_value_maps_to_a_distinct_code_from_the_sql_list()
    {
        var sqlCodes = ReadCodesFromSql();
        var mapped = Enum.GetValues<HistoryEventType>().Select(HistoryEventCodes.ToCode).ToList();

        mapped.ShouldBeSubsetOf(sqlCodes);
        mapped.Distinct().Count().ShouldBe(mapped.Count);
        mapped.Count.ShouldBe(sqlCodes.Count);
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
