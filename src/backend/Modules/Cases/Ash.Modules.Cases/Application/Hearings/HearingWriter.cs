using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Hearings;

/// <summary>
/// What every hearing change shares (task 030): checking a time and a type, the warnings, the API view, and keeping
/// the case's derived columns right in the same transaction:
/// - cases.next_hearing_at = MIN(scheduled_at) of the SCHEDULED hearings, past ones INCLUDED (a stored value must not
///   depend on "now"; an overdue hearing stays the next one until it is completed); NULL when there is none;
/// - cases.last_hearing_id = the HELD hearing with the latest scheduled_at (the reports show its description); NULL.
/// Both are written with their own UPDATE: cases.row_version is NOT changed by hearings (derived data).
/// </summary>
internal sealed class HearingWriter(CasesDbContext db, IReferenceData reference, IUserDirectory users, IClock clock)
{
    public const int MaxDescriptionLength = 4000;
    public static readonly TimeSpan CloseTo = TimeSpan.FromMinutes(30);

    public const string AddPermission = "hearing.add";
    public const string EditPermission = "hearing.edit";

    public static readonly string[] Outcomes = [CaseHearing.Held, CaseHearing.Postponed, CaseHearing.Cancelled];

    /// <summary>
    /// The type by code (active), with its text when it is the "other" type ("Tjetër"); errors under
    /// <paramref name="prefix"/> + hearingTypeCode / otherTypeText. Null when invalid.
    /// </summary>
    public async Task<HearingTypeDto?> ResolveTypeAsync(
        string? code, string? otherTypeText, string prefix, FieldErrors errors, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            errors.Add($"{prefix}hearingTypeCode", "Zgjidhni llojin e seancës.");
            return null;
        }

        var type = await reference.GetHearingType(code.Trim(), ct);
        if (type is null || !type.IsActive)
        {
            errors.Add($"{prefix}hearingTypeCode", "Lloji i seancës nuk ekziston ose nuk është aktiv.");
            return null;
        }

        if (type.IsOther && string.IsNullOrWhiteSpace(otherTypeText))
        {
            errors.Add($"{prefix}otherTypeText", "Plotësoni llojin e seancës.");
            return null;
        }

        if (otherTypeText is { } text && text.Trim().Length > RegisterCaseValidator.MaxOtherTypeLength)
        {
            errors.Add($"{prefix}otherTypeText", $"Përshkrimi i llojit mund të ketë deri në {RegisterCaseValidator.MaxOtherTypeLength} karaktere.");
            return null;
        }

        return type;
    }

    public static string? OtherText(HearingTypeDto type, string? text) => type.IsOther ? text!.Trim() : null;

    public static void CheckDescription(string? description, string field, FieldErrors errors)
    {
        if (description is not null && description.Trim().Length > MaxDescriptionLength)
        {
            errors.Add(field, $"Përshkrimi mund të ketë deri në {MaxDescriptionLength} karaktere.");
        }
    }

    public static string? CleanDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    /// <summary>A time in the past, or another SCHEDULED hearing of the case within ±30 minutes: warnings, not errors.</summary>
    public async Task AddWarningsAsync(ulong caseId, DateTime atUtc, ulong? exceptHearingId, List<RegistrationWarning> warnings, CancellationToken ct)
    {
        if (atUtc < clock.UtcNow)
        {
            warnings.Add(new RegistrationWarning("hearing.in_past",
                "Data e seancës është në të kaluarën. Kontrolloni nëse është regjistrim i vonuar."));
        }

        var from = atUtc - CloseTo;
        var to = atUtc + CloseTo;
        var close = await db.CaseHearings.AsNoTracking()
            .Where(h => h.CaseId == caseId && h.Status == CaseHearing.Scheduled && h.Id != (exceptHearingId ?? 0)
                        && h.ScheduledAt >= from && h.ScheduledAt <= to)
            .OrderBy(h => h.ScheduledAt)
            .Select(h => (DateTime?)h.ScheduledAt)
            .FirstOrDefaultAsync(ct);
        if (close is { } other)
        {
            warnings.Add(new RegistrationWarning("hearing.close_to_another",
                $"Kjo çështje ka një seancë tjetër të planifikuar brenda 30 minutave ({Local(other)})."));
        }
    }

    /// <summary>next_hearing_at and last_hearing_id from the hearings as they are now (call after SaveChanges).</summary>
    public async Task RecalculateAsync(ulong caseId, CancellationToken ct)
    {
        var next = await db.CaseHearings
            .Where(h => h.CaseId == caseId && h.Status == CaseHearing.Scheduled)
            .MinAsync(h => (DateTime?)h.ScheduledAt, ct);
        var last = await db.CaseHearings
            .Where(h => h.CaseId == caseId && h.Status == CaseHearing.Held)
            .OrderByDescending(h => h.ScheduledAt).ThenByDescending(h => h.Id)
            .Select(h => (ulong?)h.Id)
            .FirstOrDefaultAsync(ct);
        await db.Cases.Where(c => c.Id == caseId).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.NextHearingAt, next)
            .SetProperty(c => c.LastHearingId, last), ct);
    }

    /// <summary>The API view of hearings: type names and people's names in one call each.</summary>
    public async Task<List<HearingView>> ViewsAsync(IReadOnlyCollection<CaseHearing> hearings, CancellationToken ct)
    {
        var types = (await reference.ListHearingTypesAsync(ct)).ToDictionary(t => t.Id);
        var people = await users.GetNamesAsync(
            hearings.Select(h => (long)h.CreatedBy).Concat(hearings.Where(h => h.UpdatedBy is not null).Select(h => (long)h.UpdatedBy!.Value))
                .Distinct().ToList(), ct);
        var now = clock.UtcNow;
        return hearings.Select(h => new HearingView(
                h.PublicId,
                types.TryGetValue(h.HearingTypeId, out var type) ? new CodeName(type.Code, type.Name) : new CodeName(string.Empty, string.Empty),
                h.OtherTypeText,
                DateTime.SpecifyKind(h.ScheduledAt, DateTimeKind.Utc),
                h.Status,
                h.Description,
                people.GetValueOrDefault(h.CreatedBy),
                h.UpdatedBy is { } u ? people.GetValueOrDefault(u) : null,
                h.RowVersion,
                h.Status == CaseHearing.Scheduled && h.ScheduledAt < now))
            .ToList();
    }

    public async Task<HearingView> ViewAsync(CaseHearing hearing, CancellationToken ct) => (await ViewsAsync([hearing], ct))[0];

    /// <summary>"dd.MM.yyyy HH:mm" in Tirane, for history summaries and messages.</summary>
    public static string Local(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TiraneTime.Zone).ToString("dd.MM.yyyy HH:mm");

    public static Error HearingNotFound() => Error.NotFound("hearing.not_found", "Seanca nuk u gjet.");

    /// <summary>
    /// A stale hearing row_version: the same code as every other stale save (case.changed), so the UI shows its one
    /// ConflictAlert with "Rifresko".
    /// </summary>
    public static Error HearingChanged() => AssignLawyersHandler.Changed();
}
