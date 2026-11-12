namespace Ash.Modules.Reports.Infrastructure;

/// <summary>
/// The statistics statements (task 044), put together ONCE when the type loads from the embedded files under
/// Infrastructure/Sql: "WITH live, base, scoped, f" + one query. They are constants from then on — no request ever builds
/// SQL text; every value (year bounds, filters, the measure of a drill-down) is a bound parameter.
/// </summary>
public static class StatisticsSql
{
    private static readonly string Live = Load("StatisticsLive.sql");
    private static readonly string Flags = Load("StatisticsFlags.sql");

    /// <summary>The CTEs every statistics query starts with (live → base → scoped → f).</summary>
    private static readonly string With = string.Concat("WITH\n", Live, ",\n", Flags, "\n");

    public static readonly string Table = string.Concat(With, Load("StatisticsTable.sql"));
    public static readonly string Money = string.Concat(With, Load("StatisticsMoney.sql"));
    public static readonly string Results = string.Concat(With, Load("StatisticsResults.sql"));
    public static readonly string InHand = string.Concat(With, Load("StatisticsInHand.sql"));
    public static readonly string Cases = string.Concat(With, Load("StatisticsCases.sql"));

    /// <summary>How many cases are behind the number (the drill-down's total): the same statement, counted.</summary>
    public static readonly string CasesCount = string.Concat(With, "SELECT COUNT(*) FROM (", Load("StatisticsCases.sql"), ") AS behind");

    public static readonly string CaseDetails = Load("StatisticsCaseDetails.sql");

    /// <summary>INSERT … WITH live … SELECT: MySQL takes the WITH between the column list and the SELECT.</summary>
    public static readonly string SnapshotInsert = Load("StatisticsSnapshotInsert.sql")
        .Replace("SELECT @year,", string.Concat("WITH\n", Live, "\nSELECT @year,"), StringComparison.Ordinal);

    private static string Load(string name)
    {
        var assembly = typeof(StatisticsSql).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Sql.{name}", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
