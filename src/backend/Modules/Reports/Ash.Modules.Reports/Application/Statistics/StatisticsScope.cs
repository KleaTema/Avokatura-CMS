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
    bool IsCurrentYear)
{
    /// <summary>The cache key of a current-year answer (scope already applied, so equal keys = equal answers).</summary>
    public string CacheKey => string.Join('|', "statistics", Year, Table, OfficeId, LawyerId, InstitutionId, WithChildren);
}

/// <summary>
/// Who may see which statistics (task 044) and on which cases — the filters are FORCED, never trusted:
/// <list type="bullet">
/// <item>Monitor / Admin (report.view_all): everything, any filter.</item>
/// <item>Kryetar (report.view_office): their own office only — another officeId → 403.</item>
/// <item>Lawyer / assistant (report.view_own, office user): their own cases — the lawyer filter is forced to them.</item>
/// <item>Institution user: Tabela 6 of their own institution only (Tabela 5 or another institution → 403).</item>
/// <item>Protokoll, institution jurists, anyone else → 403.</item>
/// </list>
/// TODO(client-q6): which statistics each role may see.
/// </summary>
public sealed class StatisticsScope(ICurrentUser user, IOfficeDirectory offices, IUserDirectory users, IInstitutionDirectory institutions, IClock clock)
{
    private const string InstitutionUserRole = "INSTITUTION_USER";
    private static readonly Error Forbidden = Error.Forbidden("statistics.forbidden", "Nuk keni të drejtë të shihni këto statistika.");

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

        long? officeId = null, lawyerId = null, institutionId = null;
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

        // --- the access rules: force the filters, refuse what is not theirs
        if (user.HasPermission("report.view_all"))
        {
            // every filter as given
        }
        else if (user.HasPermission("report.view_office") && user.OfficeId is { } ownOffice)
        {
            if (officeId is not null && officeId != ownOffice)
            {
                return Forbidden;
            }

            officeId = ownOffice;
        }
        else if (user.HasPermission("report.view_own") && user.InstitutionId is { } ownInstitution)
        {
            if (!user.Roles.Contains(InstitutionUserRole) || table != 6
                || (institutionId is not null && (institutionId != ownInstitution || withChildren)))
            {
                return Forbidden;
            }

            institutionId = ownInstitution;
            withChildren = false;
        }
        else if (user.HasPermission("report.view_own") && user.OfficeId is not null)
        {
            if (lawyerId is not null && lawyerId != user.UserId)
            {
                return Forbidden;
            }

            lawyerId = user.UserId;
        }
        else
        {
            return Forbidden;
        }

        if (table == 6 && institutionId is null)
        {
            return Error.Validation("statistics.institution_required", "Tabela 6 kërkon një institucion ose një ministri.");
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
            current ? clock.UtcNow : end, current);
    }
}
