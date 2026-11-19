using System.Globalization;

namespace Ash.Modules.Reports.Application.Statistics;

/// <summary>A header cell of Tabela 3 / 4 (task 053o): merged over ColSpan columns and RowSpan header rows.</summary>
public sealed record StatisticsHeaderCell(string Text, int ColSpan, int RowSpan, string? Definition = null);

/// <summary>
/// A leaf column: Key = the value's key in every row; Kind = count | m2 | money; Measure / ResultColumn / Currency tell
/// the drill-down which cases are behind a cell (Measure "demi" for the damages). Letter / Formula: the client's
/// letter-code and formula rows.
/// </summary>
public sealed record StatisticsLeaf(
    string Key, string Header, string Kind, string? Measure, string? ResultColumn, string? Currency, string? Letter, string? Formula,
    string? Definition);

/// <summary>A court row (CourtId null = the TOTAL row "TOTALI"); Values: leaf key → number (money / m² with 2 decimals).</summary>
public sealed record StatisticsGridRow(int? CourtId, string Court, IReadOnlyDictionary<string, decimal> Values);

/// <summary>
/// Task 053o — Tabela 3 / 4 as the client's file draws it (Tabelat e statistikave.xlsx): the title, the header rows with
/// their merges, the letter row, the formula row, the leaf columns and the rows. ONE structure for the web table, the
/// Excel file and the PDF, so they cannot differ (StatisticsLayoutTests compares it to the client's file).
/// </summary>
public sealed record StatisticsGrid(
    string Title,
    IReadOnlyList<IReadOnlyList<StatisticsHeaderCell>> HeaderRows,
    IReadOnlyList<StatisticsLeaf> Columns,
    IReadOnlyList<StatisticsGridRow> Rows);

/// <summary>The open points of the client's table, read from ash_core.report_settings (script 034; provisional).</summary>
public sealed record StatisticsLayoutSettings(bool SplitAppeal, bool ShowCarriedOrphan);

/// <summary>A result column of "Rezultati i çështjeve të përfunduara" (ash_core.statistics_result_columns).</summary>
public sealed record StatisticsResultColumn(string Code, string Label);

/// <summary>
/// Builds <see cref="StatisticsGrid"/>: the client's column tree (texts as in the file, with the Albanian letters ë / ç
/// and lower-case "i" where the file writes "e" / "c" / "I"), the result columns and the damages' currencies from data.
/// </summary>
public static class StatisticsLayout
{
    public const string TotalRow = "TOTALI";
    public const string CourtHeader = "Gjykata";

    /// <summary>A node of the column tree: a group (Children) or a leaf.</summary>
    private sealed record Node(string Text, IReadOnlyList<Node>? Children = null, StatisticsLeaf? Leaf = null, string? Definition = null);

    private static Node Group(string text, params Node[] children) => new(text, children);

    private static Node Count(string text, string measure, string? letter, string? formula = null) =>
        new(text, Leaf: new StatisticsLeaf(measure, text, "count", measure, null, null, letter, formula, Definition(measure)));

    private static string? Definition(string measure) =>
        StatisticsMeasures.All.FirstOrDefault(m => m.Code == measure)?.Definition;

    /// <summary>The Avokat | Delegim/Relatim pair of the client's file under one header.</summary>
    private static Node Pair(string text, string ashMeasure, string delegMeasure, string? ashLetter, string? delegLetter,
        string ashText = "Avokat", string delegText = "Delegim/Relatim") =>
        Group(text, Count(ashText, ashMeasure, ashLetter), Count(delegText, delegMeasure, delegLetter));

    // the client's letters of the result pairs (ll, m … AA), in the result columns' order
    private static readonly string[] ResultLetters = ["ll", "m", "n", "nj", "O", "P", "Q", "R", "S", "T", "U", "V", "X", "Y", "Z", "AA"];

    public static StatisticsGrid Build(
        int table, int year, StatisticsLayoutSettings settings, IReadOnlyList<StatisticsResultColumn> resultColumns,
        IReadOnlyList<string> currencies, IReadOnlyList<StatisticsGridRow> rows)
    {
        var institutional = table == 6;
        var tree = new List<Node>
        {
            Group("Gjithsej çështje ardhur në Avokaturën e Shtetit",
                Count("Mbartur AKTIVE", "mbartur_aktive", "A"),
                Count("Ardhur GJATË VITIT", "ardhur_gjate_vitit", "B"),
                Count("TOTAL", "total", "C", "A+B")),
            Group("Lloji i çështjes",
                Count("Deleguar", "deleguar", "D"),
                Count("Relatuar", "relatuar", "E"),
                Count("Aktive", "aktive_ndjekje", "F", "C-(D+E)")),
            Group("Çështje të mbyllura",
                Pair("Përfunduar", "perfunduar_ash", "perfunduar_deleguar", "G", "H",
                    ashText: "Avokat (çështje aktive të vitit aktual, mbartur ose ardhur)")),
        };
        if (!institutional)
        {
            tree.Add(Group("Çështje të kaluara nga një Av. Shteti tek një tjetër Av. Shteti",
                Count("Transferuar si çështje", "transferuar", "i")));
        }

        tree.Add(Group("Çështje aktive në dorë aktualisht Avokati i Shtetit", Count("Në dorë", "ne_dore", "j")));
        tree.Add(Group("Çështje aktive por që janë pezulluar nga gjykata dhe pritet të ridalë për shqyrtim gjyqësor",
            Pair("Pezulluar", "pezulluar_ash", "pezulluar_deleguar", "k", "l")));

        // the results: one Avokat | Delegim/Relatim pair per column of ash_core.statistics_result_columns
        var results = resultColumns.Select((c, i) => Group(c.Label,
            new Node("Avokat", Leaf: new StatisticsLeaf($"res:{c.Code}:ash", "Avokat", "count", "perfunduar_ash", c.Code, null,
                Letter(ResultLetters, i * 2), null, Definition("perfunduar_ash"))),
            new Node("Delegim/Relatim", Leaf: new StatisticsLeaf($"res:{c.Code}:del", "Delegim/Relatim", "count", "perfunduar_deleguar", c.Code, null,
                Letter(ResultLetters, i * 2 + 1), null, Definition("perfunduar_deleguar"))))).ToArray();
        tree.Add(Group("Rezultati i çështjeve të përfunduara", results));

        // the damages: m² and one money column per currency (never summed across currencies)
        tree.Add(Group(string.Empty,
            Damage("Dëmi i parandaluar", "dp", currencies, "ab", "ac"),
            Damage("Dëmi shkaktuar", "dc", currencies, "ad", "ae")));
        if (settings.ShowCarriedOrphan)
        {
            tree.Add(Group(string.Empty, Count("Mbartur nga vitet e kaluara ardhur vendimi", "mbartur_ardhur_vendim", "ag")));
        }

        tree.Add(settings.SplitAppeal
            ? Group("Çështje të mbyllura me ardhjen e vendimit të arsyetuar të cilat janë objekt ankimi",
                Count("Ankimuar", "ankimuar", "ai"), Count("Rekurs", "rekursuar", "aj"))
            : Group("Çështje të mbyllura me ardhjen e vendimit të arsyetuar të cilat janë objekt ankimi",
                Count("Ankimuar / Rekursuar", "ankimuar_rekursuar", "ai")));
        // the client's file says "që ka nuk ka ardhur" (a slip): "që nuk ka ardhur"
        tree.Add(Group("Çështje që priten të ankimohen gjatë vitit për çështjet e mbyllura në vitet e kaluara që janë mbyllur për efekt statistike dhe që nuk ka ardhur vendimi i zbardhur",
            Count("Mbartur nga vitet e kaluara në pritje të vendimit të arsyetuar", "mbartur_pritje_vendimi", "ak")));
        tree.Add(Group("Çështje të ankimuara gjatë vitit për çështjet e mbyllura në vitet e kaluara që janë mbyllur për efekt statistike dhe që ka ardhur vendimi i zbardhur",
            Count("Mbartur nga vitet e kaluara ardhur vendimi", "mbartur_ardhur_vendim", "al")));

        const int depth = 3;
        var headerRows = Enumerable.Range(0, depth).Select(_ => new List<StatisticsHeaderCell>()).ToList();
        // the first column: the court, over every header row
        headerRows[0].Add(new StatisticsHeaderCell(CourtHeader, 1, depth));
        var leaves = new List<StatisticsLeaf>();
        foreach (var node in tree)
        {
            Place(node, 0, depth, headerRows, leaves);
        }

        var title = institutional ? Builder.ReportTables.InstitutionalTitle : Builder.ReportTables.StatisticsTitle;
        return new StatisticsGrid($"{title} — {year.ToString(CultureInfo.InvariantCulture)}",
            headerRows.Select(r => (IReadOnlyList<StatisticsHeaderCell>)r).ToList(), leaves, rows);
    }

    private static string? Letter(string[] letters, int index) => index < letters.Length ? letters[index] : null;

    private static Node Damage(string text, string prefix, IReadOnlyList<string> currencies, string m2Letter, string moneyLetter)
    {
        var definition = StatisticsMeasures.DamageMeasure.Definition;
        var children = new List<Node>
        {
            new("m2", Leaf: new StatisticsLeaf($"{prefix}:m2", "m2", "m2", StatisticsMeasures.Damage, null, null, m2Letter, null, definition)),
        };
        var list = currencies.Count == 0 ? [string.Empty] : currencies;
        children.AddRange(list.Select((c, i) =>
        {
            var header = list.Count == 1 ? "MONEDHË" : $"MONEDHË {c}";
            return new Node(header, Leaf: new StatisticsLeaf($"{prefix}:{c}", header, "money", StatisticsMeasures.Damage, null,
                c == string.Empty ? null : c, i == 0 ? moneyLetter : null, null, definition));
        }));
        return new Node(text, children);
    }

    /// <summary>Puts a node into the header rows: a group spans its leaves; a leaf reaches down to the last header row.</summary>
    private static void Place(Node node, int level, int depth, List<List<StatisticsHeaderCell>> rows, List<StatisticsLeaf> leaves)
    {
        if (node.Leaf is { } leaf)
        {
            rows[level].Add(new StatisticsHeaderCell(node.Text, 1, depth - level, leaf.Definition));
            leaves.Add(leaf);
            return;
        }

        var children = node.Children!;
        rows[level].Add(new StatisticsHeaderCell(node.Text, LeafCount(node), 1, node.Definition));
        foreach (var child in children)
        {
            Place(child, level + 1, depth, rows, leaves);
        }
    }

    private static int LeafCount(Node node) => node.Leaf is not null ? 1 : node.Children!.Sum(LeafCount);
}
