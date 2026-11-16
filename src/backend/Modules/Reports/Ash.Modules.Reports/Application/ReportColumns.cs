namespace Ash.Modules.Reports.Application;

/// <summary>How a column is written to Excel: text, a date, a date and time (hearings), money / m² (2 decimals), yes/no.</summary>
public enum ReportColumnKind
{
    Text,
    LongText,
    Date,
    DateTime,
    Decimal,
    YesNo,
    /// <summary>A count (the statistics).</summary>
    Integer,
}

/// <summary>One column of a report: the JSON property it reads, its Albanian header, how it is written, its width.</summary>
public sealed record ReportColumn(string Key, string Header, ReportColumnKind Kind, double Width);

/// <summary>
/// The Albanian column headers of the two reports — the ONE place (the JSON lists them for the UI of task 045; the
/// export writes them). Named after the client's tables (Tabela 1 / Tabela 3) and the report views' columns.
/// TODO(client-q-report-headers): confirm the exact wording against the client's memo.
/// </summary>
public static class ReportColumns
{
    public const string ActiveReportName = "Raporti i çështjeve aktive (Tabela 1)";
    public const string ClosedReportName = "Raporti i çështjeve të mbyllura (Tabela 3)";

    public static readonly IReadOnlyList<ReportColumn> Active =
    [
        new("caseCode", "Kodi unik", ReportColumnKind.Text, 16),
        new("actNumber", "Numri i aktit", ReportColumnKind.Text, 14),
        new("plaintiffs", "Paditësi", ReportColumnKind.LongText, 30),
        new("defendants", "I padituri / Person i tretë", ReportColumnKind.LongText, 34),
        new("objectText", "Objekti", ReportColumnKind.LongText, 48),
        new("court", "Gjykata", ReportColumnKind.Text, 30),
        new("judge", "Gjyqtari", ReportColumnKind.Text, 22),
        new("nextHearingAt", "Seanca e radhës", ReportColumnKind.DateTime, 17),
        new("lawyers", "Avokati", ReportColumnKind.Text, 30),
        new("lastHearingDescription", "Përshkrimi i seancës së fundit", ReportColumnKind.LongText, 40),
        new("hasSecurityMeasure", "Masa e sigurimit", ReportColumnKind.YesNo, 10),
        new("claimAmount", "Vlera e padisë", ReportColumnKind.Decimal, 16),
        new("claimCurrency", "Monedha", ReportColumnKind.Text, 9),
        new("claimAreaM2", "Sipërfaqja (m²)", ReportColumnKind.Decimal, 14),
        new("category", "Kategoria", ReportColumnKind.Text, 18),
        new("subcategory", "Lloji", ReportColumnKind.Text, 20),
        new("priority", "Prioriteti", ReportColumnKind.Text, 11),
        new("isSuspended", "Pezulluar", ReportColumnKind.YesNo, 10),
        new(ReportMyRole.ColumnKey, "Roli im", ReportColumnKind.Text, 16), // task 053h
    ];

    public static readonly IReadOnlyList<ReportColumn> Closed =
    [
        new("caseCode", "Kodi unik", ReportColumnKind.Text, 16),
        new("actNumber", "Numri i aktit", ReportColumnKind.Text, 14),
        new("courtLevel", "Shkalla", ReportColumnKind.Text, 16),
        new("court", "Gjykata", ReportColumnKind.Text, 30),
        new("judge", "Gjyqtari", ReportColumnKind.Text, 22),
        new("plaintiffs", "Paditësi", ReportColumnKind.LongText, 30),
        new("defendants", "I padituri / Person i tretë", ReportColumnKind.LongText, 34),
        new("objectText", "Objekti", ReportColumnKind.LongText, 48),
        new("lawyer", "Avokati", ReportColumnKind.Text, 24),
        new("result", "Rezultati", ReportColumnKind.Text, 22),
        new("resultOtherText", "Rezultati (tjetër)", ReportColumnKind.LongText, 24),
        new("decisionNumber", "Numri i vendimit", ReportColumnKind.Text, 14),
        new("decisionDate", "Data e vendimit", ReportColumnKind.Date, 13),
        new("dispositive", "Dispozitivi", ReportColumnKind.LongText, 48),
        new("currency", "Monedha", ReportColumnKind.Text, 9),
        new("wonAmount", "Fituar (vlera)", ReportColumnKind.Decimal, 15),
        new("wonAreaM2", "Fituar (m²)", ReportColumnKind.Decimal, 12),
        new("lostAmount", "Humbur (vlera)", ReportColumnKind.Decimal, 15),
        new("lostAreaM2", "Humbur (m²)", ReportColumnKind.Decimal, 12),
        new("preventedDamageAmount", "Dëm i parandaluar (vlera)", ReportColumnKind.Decimal, 16),
        new("preventedDamageAreaM2", "Dëm i parandaluar (m²)", ReportColumnKind.Decimal, 14),
        new("causedDamageAmount", "Dëm i shkaktuar (vlera)", ReportColumnKind.Decimal, 16),
        new("causedDamageAreaM2", "Dëm i shkaktuar (m²)", ReportColumnKind.Decimal, 14),
        new("courtCostsAmount", "Shpenzime gjyqësore", ReportColumnKind.Decimal, 15),
        new("hasSecurityMeasure", "Masa e sigurimit", ReportColumnKind.YesNo, 10),
        new("isAppealed", "Ankimuar", ReportColumnKind.YesNo, 10),
        new("isRecursed", "Rekursuar", ReportColumnKind.YesNo, 10),
        new("isFinal", "Formë e prerë", ReportColumnKind.YesNo, 10),
        new("decisionReceived", "Vendimi i arsyetuar ardhur", ReportColumnKind.YesNo, 12),
        new("closedAt", "Data e mbylljes", ReportColumnKind.Date, 13),
        new(ReportMyRole.ColumnKey, "Roli im", ReportColumnKind.Text, 16), // task 053h
    ];

    public const string Yes = "Po";
    public const string No = "Jo";
}
