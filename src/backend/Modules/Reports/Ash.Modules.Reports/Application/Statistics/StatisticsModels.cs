namespace Ash.Modules.Reports.Application.Statistics;

/// <summary>
/// GET /api/reports/statistics?year=&amp;table=5|6&amp;officeId=&amp;lawyerId=&amp;institutionId=&amp;ministryId= (public ids);
/// courtLevelCode / filterCourtId: the builder's court level and court (task 053h).
/// </summary>
public sealed record StatisticsQuery(
    int? Year, int? Table, Guid? OfficeId, Guid? LawyerId, Guid? InstitutionId, Guid? MinistryId,
    string? CourtLevelCode = null, int? FilterCourtId = null);

/// <summary>The drill-down: the cases behind one number (one measure, narrowed like the cell).</summary>
public sealed record StatisticsCasesQuery(
    int? Year, int? Table, Guid? OfficeId, Guid? LawyerId, Guid? InstitutionId, Guid? MinistryId,
    string? Measure, int? CourtId, string? Currency, string? ResultCode, Guid? RowLawyerId, string? After, int? Limit,
    string? CourtLevelCode = null, int? FilterCourtId = null)
{
    public StatisticsQuery Statistics => new(Year, Table, OfficeId, LawyerId, InstitutionId, MinistryId, CourtLevelCode, FilterCourtId);
}

/// <summary>One court row (CourtId null = the TOTAL row "Gjithsej"); Values: measure code → count.</summary>
public sealed record StatisticsCourtRow(int? CourtId, string Court, IReadOnlyDictionary<string, int> Values);

/// <summary>Damages of the levels closed in the year for one court (CourtId null = that currency's total) in ONE currency.</summary>
public sealed record StatisticsMoneyRow(
    int? CourtId, string Court, string? Currency, decimal PreventedAmount, decimal PreventedM2, decimal CausedAmount, decimal CausedM2);

/// <summary>Rezultati: the levels closed in the year with this result, followed by ASH / delegated or related.</summary>
public sealed record StatisticsResultRow(string ResultCode, string Result, int Ash, int Deleguar);

/// <summary>Në dorë: open cases followed by ASH, per primary lawyer at the end point.</summary>
public sealed record StatisticsLawyerRow(Guid LawyerId, string Lawyer, int Count);

public sealed record StatisticsResponse(
    int Year,
    int Table,
    /// <summary>True when a closed year is read from its snapshot (frozen on 1 January).</summary>
    bool Frozen,
    DateTime? FrozenAt,
    /// <summary>"jo e ngrirë": a closed year without a snapshot, computed live from today's data.</summary>
    string? Warning,
    /// <summary>The count measures (columns of the table) with name and definition — StatisticsMeasures is their only source.</summary>
    IReadOnlyList<StatisticsMeasure> Measures,
    /// <summary>The damage columns' name and definition.</summary>
    StatisticsMeasure DamageMeasure,
    /// <summary>"Në dorë"'s name and definition.</summary>
    StatisticsMeasure InHandMeasure,
    IReadOnlyList<StatisticsCourtRow> Courts,
    IReadOnlyList<StatisticsMoneyRow> Money,
    IReadOnlyList<StatisticsResultRow> Results,
    IReadOnlyList<StatisticsLawyerRow> InHand,
    IReadOnlyList<ReportFilterText> Filters);

/// <summary>A case behind a number.</summary>
/// <param name="CanOpen">Task 053h: the user can open the case (else the code is shown without a link).</param>
public sealed record StatisticsCaseRow(
    Guid CasePublicId, string CaseCode, string Court, string? Lawyer, string Status, DateTime RegisteredAt, DateTime? ClosedAt,
    bool CanOpen);

public sealed record StatisticsCasesPage(IReadOnlyList<StatisticsCaseRow> Items, string? NextCursor, int Total);
