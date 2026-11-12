using System.Text;

namespace Ash.Modules.Reports.Application.Statistics;

/// <summary>
/// A measure of Tabela 5 / 6: its code (the SQL flag m_&lt;code&gt;, the drill-down's ?measure=), its Albanian name as in
/// the memo, its definition in plain Albanian (the UI's header tooltip) and an example.
/// </summary>
public sealed record StatisticsMeasure(string Code, string Name, string Definition, string Example = "");

/// <summary>
/// THE source of the measures' names and definitions (task 044/045): the API returns them, the UI shows them, and the
/// table of docs/statistics-definitions.md is GENERATED from here (StatisticsDefinitionsDocTests fails when the
/// committed document differs; run it with UPDATE_DOCS=1 to regenerate). When the client changes a definition, change
/// it here only. The SQL that computes each measure: Infrastructure/Sql/StatisticsFlags.sql.
/// TODO(client-q-stat-definitions): the client approves the definitions.
/// </summary>
public static class StatisticsMeasures
{
    public static readonly IReadOnlyList<StatisticsMeasure> Counts =
    [
        new("mbartur_aktive", "Mbartur aktive",
            "Çështjet e regjistruara para 1 janarit dhe të pambyllura deri më 1 janar.",
            "E regjistruar 15.12.2026, ende e hapur më 1.1.2027 → mbartur në 2027."),
        new("ardhur_gjate_vitit", "Ardhur gjatë vitit",
            "Çështjet e regjistruara gjatë vitit.",
            "E regjistruar 05.04.2027 → ardhur në 2027."),
        new("total", "Total",
            "Mbartur aktive + ardhur gjatë vitit.",
            "3 të mbartura + 1 e ardhur = 4."),
        new("aktive", "Aktive",
            "Çështjet e hapura në fund të vitit (sot për vitin në vazhdim).",
            "E mbyllur 01.03.2027 → aktive në fund të 2026, jo në 2027."),
        new("deleguar", "Deleguar",
            "Aktive, me kategori Delegim."),
        new("relatuar", "Relatuar",
            "Aktive, me kategori Relatim."),
        new("aktive_ndjekje", "Aktive në ndjekje",
            "Aktive, as të deleguara as të relatuara."),
        new("perfunduar_ash", "Përfunduar (ndjekur nga ASH)",
            "Të mbyllura gjatë vitit, jo të deleguara / relatuara.",
            "Mbyllur 10.12.2026 me vendim → përfunduar në 2026."),
        new("perfunduar_deleguar", "Përfunduar (deleguar / relatuar)",
            "Të mbyllura gjatë vitit, të deleguara ose të relatuara."),
        new("transferuar", "Transferuar",
            "Çështjet ku avokati primar u zëvendësua (transferim) gjatë vitit.",
            "Transferuar 01.02.2027 → 1 në 2027."),
        new("pezulluar_ash", "Pezulluar (ASH)",
            "Aktive, të pezulluara, jo të deleguara / relatuara."),
        new("pezulluar_deleguar", "Pezulluar (deleguar / relatuar)",
            "Aktive, të pezulluara, të deleguara ose të relatuara."),
        new("ankimuar_rekursuar", "Ankimuar / rekursuar",
            "Të mbyllura gjatë vitit, me vendim të ankimuar ose të rekursuar."),
        new("mbartur_pritje_vendimi", "Mbartur në pritje të vendimit të arsyetuar",
            "Të mbyllura para 1 janarit, për të cilat vendimi i arsyetuar nuk kishte ardhur deri më 1 janar.",
            "Mbyllur 10.12.2026, vendimi i arsyetuar ende pa ardhur → 1 në 2027."),
        new("mbartur_ardhur_vendim", "Mbartur, ardhur vendimi i arsyetuar",
            "Të mbyllura para 1 janarit, për të cilat vendimi i arsyetuar erdhi gjatë vitit.",
            "Mbyllur në 2026, vendimi erdhi 20.01.2027 → 1 në 2027."),
    ];

    public const string InHand = "ne_dore";
    public const string Damage = "demi";

    public static readonly StatisticsMeasure DamageMeasure = new(Damage, "Dëmi i parandaluar / i shkaktuar (për çdo monedhë, dhe m²)",
        "Shuma e vlerave të vendimeve të çështjeve të mbyllura gjatë vitit, veçmas për çdo monedhë.",
        "2.500.000 ALL dhe 1.000 EUR shfaqen në dy kolona, jo si një shumë.");

    public static readonly StatisticsMeasure InHandMeasure = new(InHand, "Në dorë",
        "Për çdo avokat: çështjet aktive në ndjekje (jo të deleguara / relatuara) ku ishte avokat primar në fund të vitit (sot për vitin në vazhdim).");

    /// <summary>Every measure, in the document's order.</summary>
    public static IEnumerable<StatisticsMeasure> All => Counts.Append(DamageMeasure).Append(InHandMeasure);

    /// <summary>Every code the drill-down accepts.</summary>
    public static readonly IReadOnlySet<string> DrillDownCodes =
        Counts.Select(m => m.Code).Append(InHand).Append(Damage).ToHashSet();

    public const string TotalRow = "Gjithsej";

    /// <summary>The Markdown table of docs/statistics-definitions.md (between its "measures" markers).</summary>
    public static string MarkdownTable()
    {
        var table = new StringBuilder();
        table.Append("| Shifra | Përkufizimi | Shembull |\n|---|---|---|\n");
        foreach (var m in All)
        {
            table.Append("| **").Append(m.Name).Append("** | ").Append(m.Definition).Append(" | ").Append(m.Example).Append(" |\n");
        }

        return table.ToString();
    }
}
