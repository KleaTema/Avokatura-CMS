using System.Runtime.CompilerServices;
using Ash.Modules.Reports.Application.Statistics;
using Shouldly;

namespace Ash.Tests.Unit;

/// <summary>
/// Task 045: the measures' names and definitions live in StatisticsMeasures.cs only; the API returns them and the table of
/// docs/statistics-definitions.md is generated from them. This fails when the committed document differs — run it
/// with the environment variable UPDATE_DOCS=1 to regenerate the table (between the "measures" markers).
/// </summary>
public class StatisticsDefinitionsDocTests
{
    private const string Start = "<!-- measures:start -->\n";
    private const string End = "<!-- measures:end -->";

    [Fact]
    public void Every_measure_has_a_name_and_a_definition()
    {
        foreach (var m in StatisticsMeasures.All)
        {
            m.Name.ShouldNotBeNullOrWhiteSpace(m.Code);
            m.Definition.ShouldNotBeNullOrWhiteSpace(m.Code);
        }

        StatisticsMeasures.All.Select(m => m.Code).ShouldBeUnique();
    }

    [Fact]
    public void The_definitions_document_is_generated_from_the_code()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..", "..", "..", "docs", "statistics-definitions.md"));
        File.Exists(path).ShouldBeTrue(path);
        var doc = File.ReadAllText(path).Replace("\r\n", "\n");
        var start = doc.IndexOf(Start, StringComparison.Ordinal);
        var end = doc.IndexOf(End, StringComparison.Ordinal);
        (start >= 0 && end > start).ShouldBeTrue("the document has no measures markers");

        var expected = StatisticsMeasures.MarkdownTable();
        var actual = doc[(start + Start.Length)..end];
        if (actual != expected && Environment.GetEnvironmentVariable("UPDATE_DOCS") == "1")
        {
            File.WriteAllText(path, doc[..(start + Start.Length)] + expected + doc[end..]);
            return;
        }

        actual.ShouldBe(expected, "docs/statistics-definitions.md differs from StatisticsMeasures.cs: run this test with UPDATE_DOCS=1");
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;
}
