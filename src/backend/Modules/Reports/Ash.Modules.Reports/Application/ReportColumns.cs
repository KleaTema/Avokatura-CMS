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
    /// <summary>
    /// Task 053o: an amount in its own currency — a number with 2 decimals whose ISO code comes from the row's
    /// <see cref="ReportColumn.CurrencyKey"/> (one cell "8.000,00 ALL"; Excel: a number with the code in its format).
    /// Totals are per currency, never across currencies.
    /// </summary>
    Money,
}

/// <summary>
/// One column of a report: the JSON property it reads, its Albanian header, how it is written, its width. Task 053o:
/// Group = the header above it (merged over the columns of the same group, e.g. "Vlera e padisë" over M2 | Monedha);
/// DefaultVisible = in the client's table (the default view); the others are switched on from "Kolonat".
/// </summary>
public sealed record ReportColumn(string Key, string Header, ReportColumnKind Kind, double Width)
{
    public string? Group { get; init; }
    public bool DefaultVisible { get; init; } = true;
    /// <summary>For <see cref="ReportColumnKind.Money"/>: the row's key holding the currency's ISO code.</summary>
    public string? CurrencyKey { get; init; }
}

/// <summary>
/// The columns of the two reports — the ONE place (the UI, the export and the print draw them). Task 053o: the client's
/// tables (Tabelat e raportimit.xlsx, Tabela 1 and 3 there; Tabela 1 and 2 in the app) are the default view in the
/// client's order and wording; our other columns follow, hidden by default.
/// </summary>
public static class ReportColumns
{
    public const string ActiveReportName = "Tabela 1 — Raporti i çështjeve aktive";
    public const string ClosedReportName = "Tabela 2 — Raporti i çështjeve të mbyllura";

    private static ReportColumn Extra(string key, string header, ReportColumnKind kind, double width) =>
        new(key, header, kind, width) { DefaultVisible = false };

    public static readonly IReadOnlyList<ReportColumn> Active =
    [
        // the client's table, in its order
        new("caseCode", "Kodi unik", ReportColumnKind.Text, 16),
        new("actNumber", "Nr. aktit", ReportColumnKind.Text, 14),
        new("plaintiffs", "Pala paditëse", ReportColumnKind.LongText, 30),
        new("defendants", "I padituri", ReportColumnKind.LongText, 34),
        new("objectText", "Objekti i çështjes", ReportColumnKind.LongText, 48),
        new("court", "Gjykata", ReportColumnKind.Text, 30),
        new("judge", "Gjykatësi", ReportColumnKind.Text, 22),
        new("nextHearingAt", "Seanca e radhës", ReportColumnKind.DateTime, 17),
        new("lawyers", "Avokati", ReportColumnKind.Text, 30),
        new("lastHearingDescription", "Përshkrimi i çështjes", ReportColumnKind.LongText, 40),
        new("hasSecurityMeasure", "Masa e sigurimit", ReportColumnKind.YesNo, 10),
        new("claimAreaM2", "M2", ReportColumnKind.Decimal, 12) { Group = "Vlera e padisë" },
        new("claimAmount", "Monedha", ReportColumnKind.Money, 18) { Group = "Vlera e padisë", CurrencyKey = "claimCurrency" },
        new("categoryAndType", "Kategoria dhe lloji i çështjes", ReportColumnKind.Text, 30),
        // ours, hidden by default
        Extra("claimCurrency", "Monedha (kodi)", ReportColumnKind.Text, 9),
        Extra("category", "Kategoria", ReportColumnKind.Text, 18),
        Extra("subcategory", "Lloji", ReportColumnKind.Text, 20),
        Extra("priority", "Prioriteti", ReportColumnKind.Text, 11),
        Extra("isSuspended", "Pezulluar", ReportColumnKind.YesNo, 10),
        Extra(ReportMyRole.ColumnKey, "Roli im", ReportColumnKind.Text, 16), // task 053h
    ];

    public static readonly IReadOnlyList<ReportColumn> Closed =
    [
        // the client's table, in its order ("Vlera": report_settings reports.closed_value, provisional)
        new("caseCode", "Kodi unik", ReportColumnKind.Text, 16),
        new("registeredAt", "Data e regjistrimit", ReportColumnKind.Date, 13),
        new("actNumber", "Nr. akti", ReportColumnKind.Text, 14),
        new("plaintiffs", "Pala paditëse", ReportColumnKind.LongText, 30),
        new("defendants", "Pala e paditur", ReportColumnKind.LongText, 34),
        new("objectText", "Objekti", ReportColumnKind.LongText, 48),
        new("court", "Gjykata", ReportColumnKind.Text, 30),
        new("judge", "Gjyqtari", ReportColumnKind.Text, 22),
        new("lawyer", "Avokati", ReportColumnKind.Text, 24),
        new("result", "Rezultati", ReportColumnKind.Text, 22),
        new("decisionNumber", "Vendimi", ReportColumnKind.Text, 14),
        new("decisionDate", "Data e vendimit", ReportColumnKind.Date, 13),
        new("valueAreaM2", "M2", ReportColumnKind.Decimal, 12) { Group = "Vlera" },
        new("valueAmount", "Monedha", ReportColumnKind.Money, 18) { Group = "Vlera", CurrencyKey = "valueCurrency" },
        new("appealedOrRecursed", "Ankimuar/ Rekursuar", ReportColumnKind.YesNo, 12),
        // ours, hidden by default
        Extra("courtLevel", "Shkalla", ReportColumnKind.Text, 16),
        Extra("resultOtherText", "Rezultati (tjetër)", ReportColumnKind.LongText, 24),
        Extra("dispositive", "Dispozitivi", ReportColumnKind.LongText, 48),
        Extra("currency", "Monedha e vendimit", ReportColumnKind.Text, 9),
        Extra("wonAmount", "Fituar (vlera)", ReportColumnKind.Decimal, 15),
        Extra("wonAreaM2", "Fituar (m²)", ReportColumnKind.Decimal, 12),
        Extra("lostAmount", "Humbur (vlera)", ReportColumnKind.Decimal, 15),
        Extra("lostAreaM2", "Humbur (m²)", ReportColumnKind.Decimal, 12),
        Extra("preventedDamageAmount", "Dëm i parandaluar (vlera)", ReportColumnKind.Decimal, 16),
        Extra("preventedDamageAreaM2", "Dëm i parandaluar (m²)", ReportColumnKind.Decimal, 14),
        Extra("causedDamageAmount", "Dëm i shkaktuar (vlera)", ReportColumnKind.Decimal, 16),
        Extra("causedDamageAreaM2", "Dëm i shkaktuar (m²)", ReportColumnKind.Decimal, 14),
        Extra("courtCostsAmount", "Shpenzime gjyqësore", ReportColumnKind.Decimal, 15),
        Extra("hasSecurityMeasure", "Masa e sigurimit", ReportColumnKind.YesNo, 10),
        Extra("isAppealed", "Ankimuar", ReportColumnKind.YesNo, 10),
        Extra("isRecursed", "Rekursuar", ReportColumnKind.YesNo, 10),
        Extra("isFinal", "Formë e prerë", ReportColumnKind.YesNo, 10),
        Extra("decisionReceived", "Vendimi i arsyetuar ardhur", ReportColumnKind.YesNo, 12),
        Extra("closedAt", "Data e mbylljes", ReportColumnKind.Date, 13),
        Extra(ReportMyRole.ColumnKey, "Roli im", ReportColumnKind.Text, 16), // task 053h
    ];

    public const string Yes = "Po";
    public const string No = "Jo";
}
