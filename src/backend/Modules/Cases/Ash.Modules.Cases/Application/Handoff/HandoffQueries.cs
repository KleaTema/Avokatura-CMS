using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.RecordDecision;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Handoff;

public sealed record OfficeRef(Guid PublicId, string Name);

/// <summary>
/// The office a court's handoff goes to (null: no office covers the court — then <see cref="Offices"/> lists the
/// active offices to choose from, with a reason).
/// </summary>
public sealed record HandoffDestinationResponse(OfficeRef? Office, IReadOnlyList<OfficeRef> Offices);

/// <summary>The open handoff of a closed level, as the case page shows it (Vendimi tab).</summary>
public sealed record CaseHandoffView(
    Guid PublicId, string CourtLevelCode, string CourtLevelName, int CourtId, string CourtName, OfficeRef Office,
    string? OverrideReason, DateTime CreatedAt, bool CanChange);

/// <summary>
/// One row of "Çështjet e ardhura": registration-level data only (code, parties, object, court, result, decision
/// date) — no documents, no chat. "Vazhdo" opens the next-level form with the destination's level and court.
/// </summary>
public sealed record IncomingHandoffItem(
    Guid HandoffPublicId,
    Guid CasePublicId,
    string CaseCode,
    IReadOnlyList<string> PartyNames,
    string ObjectText,
    string FromCourtName,
    string? ResultName,
    DateOnly? DecisionDate,
    string CourtLevelCode,
    string CourtLevelName,
    int CourtId,
    string CourtName,
    DateTime HandedOffAt);

public sealed record IncomingHandoffsResponse(IReadOnlyList<IncomingHandoffItem> Items, int Total);

internal sealed class HandoffQueries(
    CasesDbContext db,
    ICaseAccessService access,
    IReferenceData reference,
    IOfficeDirectory offices,
    ICurrentUser user)
{
    public const int MaxIncoming = 200;
    public const string LinkPreviousPermission = "case.link_previous";

    public async Task<HandoffDestinationResponse> DestinationAsync(int courtId, CancellationToken ct)
    {
        if (await offices.GetByCourtAsync(courtId, ct) is { } office)
        {
            return new HandoffDestinationResponse(new OfficeRef(office.PublicId, office.Name), []);
        }

        var active = await offices.ListActiveAsync(ct);
        return new HandoffDestinationResponse(null,
            active.OrderBy(o => o.Name).Select(o => new OfficeRef(o.PublicId, o.Name)).ToList());
    }

    /// <summary>The open handoff of the case; null when none (final, not closed, accepted, cancelled). 404 without view.</summary>
    public async Task<Result<CaseHandoffView?>> ForCaseAsync(Guid casePublicId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        var open = await db.CaseHandoffs.AsNoTracking()
            .SingleOrDefaultAsync(h => h.FromCaseId == c.Id && h.AcceptedCaseId == null && h.CancelledAt == null, ct);
        if (open is null)
        {
            return (CaseHandoffView?)null;
        }

        var canChange = (await access.GetEffectivePermissionsAsync((long)c.Id, ct)).Contains(DecisionSupport.Permission);
        return await ViewAsync(open, canChange, ct);
    }

    /// <summary>The open handoffs to the user's office, newest first (at most <see cref="MaxIncoming"/>), with the total.</summary>
    public async Task<Result<IncomingHandoffsResponse>> IncomingAsync(CancellationToken ct)
    {
        if (!user.HasPermission(LinkPreviousPermission) || user.OfficeId is not { } officeId)
        {
            return AssignLawyersHandler.Forbidden();
        }

        var open = Open(officeId);
        var total = await open.CountAsync(ct);
        var rows = await open.OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).Take(MaxIncoming).ToListAsync(ct);
        var caseIds = rows.Select(h => h.FromCaseId).ToList();
        var cases = await db.Cases.AsNoTracking().Where(c => caseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var decisions = await db.CaseDecisions.AsNoTracking().Where(d => caseIds.Contains(d.CaseId))
            .ToDictionaryAsync(d => d.CaseId, ct);
        var parties = await db.CaseParties.AsNoTracking()
            .Where(p => caseIds.Contains(p.CaseId) && p.DeletedAt == null)
            .OrderBy(p => p.Id)
            .Select(p => new { p.CaseId, p.DisplayName })
            .ToListAsync(ct);
        var results = (await reference.ListDecisionResultsAsync(ct)).ToDictionary(r => r.Id);
        var levels = (await reference.ListCourtLevelsAsync(ct)).ToDictionary(l => l.Id);

        var items = new List<IncomingHandoffItem>();
        foreach (var h in rows)
        {
            var c = cases[h.FromCaseId];
            var decision = decisions.GetValueOrDefault(h.FromCaseId);
            items.Add(new IncomingHandoffItem(
                h.PublicId, c.PublicId, c.CaseCode,
                parties.Where(p => p.CaseId == c.Id).Select(p => p.DisplayName).ToList(),
                c.ObjectText,
                (await reference.GetCourt(c.CourtId, ct))?.Name ?? string.Empty,
                decision is null ? null : results.GetValueOrDefault(decision.ResultId)?.Name,
                decision?.DecisionDate,
                levels.GetValueOrDefault(h.CourtLevelId)?.Code ?? string.Empty,
                levels.GetValueOrDefault(h.CourtLevelId)?.Name ?? string.Empty,
                h.CourtId,
                (await reference.GetCourt(h.CourtId, ct))?.Name ?? string.Empty,
                h.CreatedAt));
        }

        return new IncomingHandoffsResponse(items, total);
    }

    /// <summary>How many open handoffs wait for the office (the tab's count).</summary>
    public Task<int> IncomingCountAsync(long officeId, CancellationToken ct) => Open(officeId).CountAsync(ct);

    private IQueryable<CaseHandoff> Open(long officeId)
    {
        var office = (ushort)officeId;
        return db.CaseHandoffs.AsNoTracking()
            .Where(h => h.ToOfficeId == office && h.AcceptedCaseId == null && h.CancelledAt == null);
    }

    public async Task<CaseHandoffView> ViewAsync(CaseHandoff h, bool canChange, CancellationToken ct)
    {
        var level = await reference.GetCourtLevel(h.CourtLevelId, ct);
        var court = await reference.GetCourt(h.CourtId, ct);
        var office = await offices.GetByIdAsync(h.ToOfficeId, ct);
        return new CaseHandoffView(h.PublicId, level?.Code ?? string.Empty, level?.Name ?? string.Empty, h.CourtId,
            court?.Name ?? string.Empty, new OfficeRef(office?.PublicId ?? Guid.Empty, office?.Name ?? string.Empty),
            h.OverrideReason, h.CreatedAt, canChange);
    }
}

/// <summary>A new destination for the open handoff (same fields as the closing dialog).</summary>
public sealed record ChangeHandoffRequest(string? NextCourtLevelCode, int? NextCourtId, Guid? ReceivingOfficePublicId, string? Reason);

/// <summary>
/// PUT /api/cases/{id}/handoff (task 053c): whoever has decision.record on the closed level corrects the destination
/// until the next level is registered. 404 without view; 409 when there is no open handoff; 403 without the right.
/// </summary>
internal sealed class ChangeHandoffHandler(CasesDbContext db, ICaseAccessService access, HandoffService handoffs, HandoffQueries queries)
{
    public async Task<Result<CaseHandoffView>> Handle(Guid casePublicId, ChangeHandoffRequest req, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.PublicId == casePublicId, ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        if (await handoffs.OpenAsync(c.Id, ct) is not { } open)
        {
            return Error.Conflict("handoff.not_open",
                "Çështja nuk ka përcjellje të hapur: ose është pranuar (shkalla e radhës u regjistrua), ose vendimi është i formës së prerë.");
        }

        if (!(await access.GetEffectivePermissionsAsync((long)c.Id, ct)).Contains(DecisionSupport.Permission))
        {
            return AssignLawyersHandler.Forbidden();
        }

        var to = await handoffs.ResolveAsync(new HandoffInput(req.NextCourtLevelCode, req.NextCourtId, req.ReceivingOfficePublicId, req.Reason), ct);
        if (to.IsFailure)
        {
            return to.Error!;
        }

        await handoffs.ChangeAsync(c, open, to.Value, ct);
        var current = await handoffs.OpenAsync(c.Id, ct);
        return await queries.ViewAsync(current!, true, ct);
    }
}
