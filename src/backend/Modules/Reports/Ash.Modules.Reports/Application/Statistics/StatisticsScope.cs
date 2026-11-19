using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;

namespace Ash.Modules.Reports.Application.Statistics;

/// <summary>The filters after the access rules: internal ids, the year's bounds (UTC) and its end point.</summary>
public sealed record StatisticsFilter(
    int Year,
    int Table,
    long? OfficeId,
    long? LawyerId,
    long? InstitutionId,
    bool WithChildren,
    DateTime YearStartUtc,
    DateTime YearEndUtc,
    DateTime PointUtc,
    bool IsCurrentYear,
    long? ScopeHeadId = null,
    long? ScopeOfficeId = null,
    IReadOnlyList<long>? LawyerSet = null,
    long? ScopeInstitutionId = null,
    bool ScopeInstitutionChildren = false,
    string? CourtLevelCode = null,
    int? FilterCourtId = null)
{
    /// <summary>
    /// The lawyers whose primary cases count (task 053h): null = every lawyer (unless <see cref="LawyerId"/>);
    /// a lawyer, or an assistant's linked lawyers.
    /// </summary>
    public IReadOnlyList<long>? Lawyers => LawyerSet ?? (LawyerId is { } one ? [one] : null);

    /// <summary>The cache key of a current-year answer (scope already applied, so equal keys = equal answers).</summary>
    public string CacheKey =>
        string.Join('|', "statistics", Year, Table, OfficeId, LawyerId, InstitutionId, WithChildren, ScopeHeadId, ScopeOfficeId,
            Lawyers is null ? "*" : string.Join(',', Lawyers.Order()), ScopeInstitutionId, ScopeInstitutionChildren,
            CourtLevelCode, FilterCourtId);
}

/// <summary>
/// Who may see which statistics and on which cases — the filters are FORCED, never trusted. The reader comes from
/// <see cref="ReportAudience"/> (task 053h, decisions F, P6/P9 — the same rules as the reports):
/// <list type="bullet">
/// <item>Monitor / Admin: everything, any filter.</item>
/// <item>Kryetar: own office + supervised cases + own assignments (053d); the office filter only their own office.</item>
/// <item>Lawyer / assistant: the primary cases of their lawyers (himself / the current linked lawyers); the lawyer
/// filter only one of them, another → 403.</item>
/// <item>Institution user and jurist: Tabela 5 and 6 on the institution's cases (a ministry: with its direct agencies);
/// the institution filter is fixed to it, another → 403.</item>
/// <item>SECONDARY_LAWYER only (053i), Protokoll, anyone else → 403.</item>
/// </list>
/// </summary>
public sealed class StatisticsScope(
    ReportAudienceResolver audiences, IOfficeDirectory offices, IUserDirectory users, IInstitutionDirectory institutions, IClock clock)
{
    private static readonly Error Forbidden = ReportAudience.StatisticsForbidden;

    public async Task<Result<StatisticsFilter>> ResolveAsync(StatisticsQuery q, CancellationToken ct)
    {
        var thisYear = TiraneTime.Today(clock).Year;
        var year = q.Year ?? thisYear;
        if (year < 2000 || year > thisYear)
        {
            return Error.Validation("statistics.year_invalid", $"Viti duhet të jetë ndërmjet 2000 dhe {thisYear}.");
        }

        var table = q.Table ?? 5;
        if (table is not (5 or 6))
        {
            return Error.Validation("statistics.table_invalid", "Tabela duhet të jetë 5 ose 6.");
        }

        long? officeId = null, lawyerId = null, institutionId = null, scopeHead = null, scopeOffice = null;
        var withChildren = false;
        if (q.OfficeId is { } office)
        {
            officeId = await offices.GetIdAsync(office, ct);
            if (officeId is null)
            {
                return Error.Validation("statistics.office_not_found", "Zyra nuk u gjet.");
            }
        }

        if (q.LawyerId is { } lawyer)
        {
            var ids = await users.GetIdsAsync([lawyer], ct);
            if (!ids.TryGetValue(lawyer, out var id))
            {
                return Error.Validation("statistics.lawyer_not_found", "Avokati nuk u gjet.");
            }

            lawyerId = id;
        }

        if (q.InstitutionId is not null && q.MinistryId is not null)
        {
            return Error.Validation("statistics.institution_or_ministry", "Zgjidhni ose institucionin ose ministrinë, jo të dyja.");
        }

        if ((q.InstitutionId ?? q.MinistryId) is { } institution)
        {
            var found = (await institutions.GetByPublicIdsAsync([institution], ct)).FirstOrDefault();
            if (found is null)
            {
                return Error.Validation("statistics.institution_not_found", "Institucioni nuk u gjet.");
            }

            institutionId = found.Id;
            withChildren = q.MinistryId is not null;
        }

        // --- the access rules (ReportAudience): force the filters, refuse what is not theirs
        var resolved = await audiences.ResolveAsync(ct);
        if (resolved.IsFailure || resolved.Value.StatisticsDenied)
        {
            return Forbidden;
        }

        var audience = resolved.Value;
        IReadOnlyList<long>? lawyerSet = null;
        long? scopeInstitution = null;
        var scopeChildren = false;
        switch (audience.Kind)
        {
            case ReportAudienceKind.All:
                break; // every filter as given
            case ReportAudienceKind.Head:
                if (officeId is not null && officeId != audience.OfficeId)
                {
                    return Forbidden;
                }

                // task 053d: the Kryetar's whole case scope — the own office, the supervised cases, the own
                // assignments — as the case list (CaseScopeSpec); the office filter (own office only) narrows it
                scopeHead = audience.UserId;
                scopeOffice = audience.OfficeId;
                break;
            case ReportAudienceKind.Institution:
                // Tabela 5 and 6 on the institution's cases only; the institution filter is fixed to it (a ministry
                // with its agencies, as Tabela 6 by ministry) — another institution → 403
                if (institutionId is not null && institutionId != audience.InstitutionId)
                {
                    return Forbidden;
                }

                scopeInstitution = audience.InstitutionId;
                scopeChildren = audience.InstitutionWithChildren;
                institutionId = audience.InstitutionId;
                withChildren = audience.InstitutionWithChildren;
                break;
            case ReportAudienceKind.Lawyers:
                // their lawyers' PRIMARY cases (himself / the current linked lawyers); the filter only one of them
                if (lawyerId is { } chosen)
                {
                    if (!audience.LawyerIds.Contains(chosen))
                    {
                        return Forbidden;
                    }

                    lawyerSet = [chosen];
                }
                else
                {
                    lawyerSet = audience.LawyerIds;
                    lawyerId = audience.LawyerIds.Count == 1 ? audience.LawyerIds[0] : null;
                }

                break;
            default:
                return Forbidden;
        }

        if (table == 6 && institutionId is null)
        {
            return Error.Validation("statistics.institution_required", "Tabela 4 kërkon një institucion ose një ministri.");
        }

        if (table == 5)
        {
            // Tabela 5 is not narrowed by institution
            institutionId = null;
            withChildren = false;
        }

        var start = TiraneTime.StartOfDayUtc(new DateOnly(year, 1, 1));
        var end = TiraneTime.StartOfDayUtc(new DateOnly(year + 1, 1, 1));
        var current = year == thisYear;
        return new StatisticsFilter(year, table, officeId, lawyerId, institutionId, withChildren, start, end,
            current ? clock.UtcNow : end, current, scopeHead, scopeOffice, lawyerSet, scopeInstitution, scopeChildren,
            string.IsNullOrWhiteSpace(q.CourtLevelCode) ? null : q.CourtLevelCode.Trim(), q.FilterCourtId);
    }
}
