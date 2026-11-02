using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Application.AssignLawyers;
using Ash.Modules.Cases.Application.GetCase;
using Ash.Modules.Cases.Application.RegisterCase;
using Ash.Modules.Cases.Application.UpdateCaseData;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Reference.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Hearings;

/// <summary>GET hearings: every hearing of the case, newest first (by scheduled time). No view → 404.</summary>
internal sealed class ListHearingsHandler(CasesDbContext db, ICaseAccessService access, HearingWriter writer)
{
    public async Task<Result<IReadOnlyList<HearingView>>> Handle(Guid casePublicId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().Where(x => x.PublicId == casePublicId).Select(x => new { x.Id }).SingleOrDefaultAsync(ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        var hearings = await db.CaseHearings.AsNoTracking()
            .Where(h => h.CaseId == c.Id)
            .OrderByDescending(h => h.ScheduledAt).ThenByDescending(h => h.Id)
            .ToListAsync(ct);
        return await writer.ViewsAsync(hearings, ct);
    }
}

/// <summary>
/// GET hearings/new-defaults: what the new hearing form starts with — the type and the description of the latest
/// hearing that has a description (the workflow: after a hearing its description is written, then the next one is
/// planned from it); the latest hearing's type when none has a description.
/// </summary>
internal sealed class NewHearingDefaultsHandler(CasesDbContext db, ICaseAccessService access, IReferenceData reference)
{
    public async Task<Result<NewHearingDefaults>> Handle(Guid casePublicId, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().Where(x => x.PublicId == casePublicId).Select(x => new { x.Id }).SingleOrDefaultAsync(ct);
        if (c is null || !await access.CanViewAsync((long)c.Id, ct))
        {
            return AssignLawyersHandler.NotFound();
        }

        var latest = db.CaseHearings.AsNoTracking().Where(h => h.CaseId == c.Id)
            .OrderByDescending(h => h.ScheduledAt).ThenByDescending(h => h.Id);
        var source = await latest.FirstOrDefaultAsync(h => h.Description != null, ct) ?? await latest.FirstOrDefaultAsync(ct);
        if (source is null)
        {
            return new NewHearingDefaults(null, null, null);
        }

        var type = (await reference.ListHearingTypesAsync(ct)).FirstOrDefault(t => t.Id == source.HearingTypeId);
        return new NewHearingDefaults(type is null ? null : new CodeName(type.Code, type.Name), source.OtherTypeText, source.Description);
    }
}

/// <summary>
/// POST hearings (hearing.add): a SCHEDULED hearing. A past time or another SCHEDULED hearing within ±30 minutes are
/// warnings (201 anyway). HEARING_ADDED, HearingScheduled; next/last hearing of the case recalculated.
/// </summary>
internal sealed class AddHearingHandler(
    CasesDbContext db, CaseDataGate gate, HearingWriter writer, FirstDataEntry firstEntry, IHistoryWriter history,
    IAuditLog audit, IEventPublisher events, ICurrentUser user)
{
    public async Task<Result<HearingSaveResponse>> Handle(Guid casePublicId, AddHearingRequest req, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var at = RegisterCaseValidator.ParseHearingAt(req.At, "at", errors);
        HearingWriter.CheckDescription(req.Description, "description", errors);
        if (errors.Any)
        {
            return errors.ToError(InvalidMessage);
        }

        var found = await gate.ForActionAsync(casePublicId, [HearingWriter.AddPermission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var type = await writer.ResolveTypeAsync(req.HearingTypeCode, req.OtherTypeText, string.Empty, errors, ct);
        if (type is null)
        {
            return errors.ToError(InvalidMessage);
        }

        var warnings = new List<RegistrationWarning>();
        await writer.AddWarningsAsync(c.Id, at!.Value, null, warnings, ct);
        var hearing = new CaseHearing
        {
            PublicId = Guid.NewGuid(),
            CaseId = c.Id,
            HearingTypeId = type.Id,
            OtherTypeText = HearingWriter.OtherText(type, req.OtherTypeText),
            ScheduledAt = at.Value,
            Status = CaseHearing.Scheduled,
            Description = HearingWriter.CleanDescription(req.Description),
            CreatedBy = (uint)user.UserId
        };
        db.CaseHearings.Add(hearing);
        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.HearingAdded,
            $"Seanca u shtua: {type.Name}, {HearingWriter.Local(hearing.ScheduledAt)}.",
            @new: new { HearingType = type.Name, hearing.OtherTypeText, hearing.ScheduledAt, hearing.Description });
        await db.SaveChangesAsync(ct);
        await writer.RecalculateAsync(c.Id, ct);

        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id, details: new { Action = "ADD_HEARING", HearingPublicId = hearing.PublicId });
        await events.PublishAsync(new HearingScheduled(c.PublicId, hearing.PublicId, hearing.ScheduledAt), ct);
        return new HearingSaveResponse(await writer.ViewAsync(hearing, ct), warnings);
    }

    public const string InvalidMessage = "Të dhënat e seancës nuk janë të vlefshme.";
}

/// <summary>
/// PUT hearings/{hearingId} (hearing.edit): a SCHEDULED hearing may be rescheduled and its type and notes changed;
/// a completed one only its description. The status is never changed here. The hearing's own row_version (409),
/// HEARING_UPDATED with the changed fields, HearingRescheduled when the time moved.
/// </summary>
internal sealed class UpdateHearingHandler(
    CasesDbContext db, CaseDataGate gate, HearingWriter writer, FirstDataEntry firstEntry, IHistoryWriter history,
    IAuditLog audit, IEventPublisher events, IReferenceData reference, ICurrentUser user)
{
    public async Task<Result<HearingSaveResponse>> Handle(Guid casePublicId, Guid hearingPublicId, UpdateHearingRequest req, CancellationToken ct)
    {
        var errors = new FieldErrors();
        DateTime? at = req.At is null ? null : RegisterCaseValidator.ParseHearingAt(req.At, "at", errors);
        HearingWriter.CheckDescription(req.Description, "description", errors);
        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i seancës. Rifreskoni faqen.");
        }

        if (errors.Any)
        {
            return errors.ToError(AddHearingHandler.InvalidMessage);
        }

        var found = await gate.ForActionAsync(casePublicId, [HearingWriter.EditPermission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var hearing = await db.CaseHearings.SingleOrDefaultAsync(h => h.PublicId == hearingPublicId && h.CaseId == c.Id, ct);
        if (hearing is null)
        {
            return HearingWriter.HearingNotFound();
        }

        if (hearing.RowVersion != req.RowVersion)
        {
            return HearingWriter.HearingChanged();
        }

        var types = (await reference.ListHearingTypesAsync(ct)).ToDictionary(t => t.Id);
        var currentType = types[hearing.HearingTypeId];
        HearingTypeDto newType = currentType;
        var typeTouched = req.HearingTypeCode is not null || req.OtherTypeText is not null;
        if (typeTouched)
        {
            var resolved = await writer.ResolveTypeAsync(req.HearingTypeCode ?? currentType.Code, req.OtherTypeText ?? hearing.OtherTypeText,
                string.Empty, errors, ct);
            if (resolved is null)
            {
                return errors.ToError(AddHearingHandler.InvalidMessage);
            }

            newType = resolved;
        }

        var newOther = typeTouched ? HearingWriter.OtherText(newType, req.OtherTypeText ?? hearing.OtherTypeText) : hearing.OtherTypeText;
        var newAt = at ?? hearing.ScheduledAt;
        var newDescription = req.Description is null ? hearing.Description : HearingWriter.CleanDescription(req.Description);
        var scheduleChanged = newAt != hearing.ScheduledAt || newType.Id != hearing.HearingTypeId || newOther != hearing.OtherTypeText;

        if (!scheduleChanged && newDescription == hearing.Description)
        {
            return CaseDataGate.NoChange();
        }

        if (scheduleChanged && hearing.Status != CaseHearing.Scheduled)
        {
            return Error.Validation("hearing.not_scheduled",
                "Seanca nuk është më e planifikuar: ndryshohet vetëm përshkrimi i saj.");
        }

        var before = Snapshot(currentType.Name, hearing.OtherTypeText, hearing.ScheduledAt, hearing.Description);
        var oldAt = hearing.ScheduledAt;
        hearing.HearingTypeId = newType.Id;
        hearing.OtherTypeText = newOther;
        hearing.ScheduledAt = newAt;
        hearing.Description = newDescription;
        hearing.UpdatedBy = (uint)user.UserId;
        hearing.RowVersion++;

        var warnings = new List<RegistrationWarning>();
        if (newAt != oldAt)
        {
            await writer.AddWarningsAsync(c.Id, newAt, hearing.Id, warnings, ct);
        }

        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.HearingUpdated,
            newAt != oldAt
                ? $"Seanca u zhvendos: {HearingWriter.Local(oldAt)} → {HearingWriter.Local(newAt)}."
                : $"Seanca e {HearingWriter.Local(newAt)} u ndryshua.",
            old: before, @new: Snapshot(newType.Name, newOther, newAt, newDescription));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HearingWriter.HearingChanged();
        }

        await writer.RecalculateAsync(c.Id, ct);
        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id, details: new { Action = "UPDATE_HEARING", HearingPublicId = hearing.PublicId });
        if (newAt != oldAt)
        {
            await events.PublishAsync(new HearingRescheduled(c.PublicId, hearing.PublicId, oldAt, newAt), ct);
        }

        return new HearingSaveResponse(await writer.ViewAsync(hearing, ct), warnings);
    }

    private static object Snapshot(string type, string? otherTypeText, DateTime scheduledAt, string? description) =>
        new { HearingType = type, OtherTypeText = otherTypeText, ScheduledAt = DateTime.SpecifyKind(scheduledAt, DateTimeKind.Utc), Description = description };
}

/// <summary>
/// POST hearings/{hearingId}/complete (hearing.edit): SCHEDULED → HELD | POSTPONED | CANCELLED (never back), with the
/// description (required for HELD and POSTPONED) and, optionally, the next SCHEDULED hearing — one transaction.
/// POSTPONED without a next hearing is allowed with a warning. HEARING_COMPLETED, HearingCompleted (+ HearingScheduled).
/// </summary>
internal sealed class CompleteHearingHandler(
    CasesDbContext db, CaseDataGate gate, HearingWriter writer, FirstDataEntry firstEntry, IHistoryWriter history,
    IAuditLog audit, IEventPublisher events, ICurrentUser user)
{
    private static readonly Dictionary<string, string> Done = new()
    {
        [CaseHearing.Held] = "u zhvillua",
        [CaseHearing.Postponed] = "u shty",
        [CaseHearing.Cancelled] = "u anulua",
    };

    public async Task<Result<CompleteHearingResponse>> Handle(Guid casePublicId, Guid hearingPublicId, CompleteHearingRequest req, CancellationToken ct)
    {
        var errors = new FieldErrors();
        var outcome = req.Outcome?.Trim();
        if (outcome is null || !HearingWriter.Outcomes.Contains(outcome))
        {
            errors.Add("outcome", "Zgjidhni si përfundoi seanca: HELD, POSTPONED ose CANCELLED.");
        }
        else if (outcome != CaseHearing.Cancelled && string.IsNullOrWhiteSpace(req.Description))
        {
            errors.Add("description", "Përshkrimi i seancës është i detyrueshëm.");
        }

        HearingWriter.CheckDescription(req.Description, "description", errors);
        DateTime? nextAt = req.Next is null ? null : RegisterCaseValidator.ParseHearingAt(req.Next.At, "next.at", errors);
        if (req.RowVersion is null)
        {
            errors.Add("rowVersion", "Mungon versioni i seancës. Rifreskoni faqen.");
        }

        if (errors.Any)
        {
            return errors.ToError(AddHearingHandler.InvalidMessage);
        }

        var found = await gate.ForActionAsync(casePublicId, [HearingWriter.EditPermission], ct);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var c = found.Value;
        var hearing = await db.CaseHearings.SingleOrDefaultAsync(h => h.PublicId == hearingPublicId && h.CaseId == c.Id, ct);
        if (hearing is null)
        {
            return HearingWriter.HearingNotFound();
        }

        if (hearing.Status != CaseHearing.Scheduled)
        {
            return Error.Conflict("hearing.already_completed", "Seanca është përfunduar tashmë; statusi i saj nuk ndryshon më.");
        }

        if (hearing.RowVersion != req.RowVersion)
        {
            return HearingWriter.HearingChanged();
        }

        HearingTypeDto? nextType = null;
        if (req.Next is not null)
        {
            nextType = await writer.ResolveTypeAsync(req.Next.HearingTypeCode, req.Next.OtherTypeText, "next.", errors, ct);
            if (nextType is null)
            {
                return errors.ToError(AddHearingHandler.InvalidMessage);
            }
        }

        // 1. this hearing
        var when = HearingWriter.Local(hearing.ScheduledAt);
        hearing.Status = outcome!;
        hearing.Description = HearingWriter.CleanDescription(req.Description) ?? hearing.Description;
        hearing.UpdatedBy = (uint)user.UserId;
        hearing.RowVersion++;

        // 2. the next one, in the same transaction
        var warnings = new List<RegistrationWarning>();
        CaseHearing? next = null;
        if (nextType is not null)
        {
            await writer.AddWarningsAsync(c.Id, nextAt!.Value, hearing.Id, warnings, ct);
            next = new CaseHearing
            {
                PublicId = Guid.NewGuid(),
                CaseId = c.Id,
                HearingTypeId = nextType.Id,
                OtherTypeText = HearingWriter.OtherText(nextType, req.Next!.OtherTypeText),
                ScheduledAt = nextAt.Value,
                Status = CaseHearing.Scheduled,
                CreatedBy = (uint)user.UserId
            };
            db.CaseHearings.Add(next);
        }
        else if (outcome == CaseHearing.Postponed)
        {
            warnings.Add(new RegistrationWarning("hearing.postponed_without_date", "Shtyrja zakonisht ka datë të re."));
        }

        history.Add((long)c.DossierId, (long)c.Id, HistoryEventType.HearingCompleted,
            next is null
                ? $"Seanca e {when} {Done[outcome!]}."
                : $"Seanca e {when} {Done[outcome!]}; seanca e radhës: {HearingWriter.Local(next.ScheduledAt)}.",
            old: new { Status = CaseHearing.Scheduled },
            @new: new
            {
                Status = outcome,
                hearing.Description,
                NextScheduledAt = next?.ScheduledAt,
                NextHearingType = nextType?.Name
            });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return HearingWriter.HearingChanged();
        }

        await writer.RecalculateAsync(c.Id, ct);
        await firstEntry.MarkAsync(c, ct);
        audit.Record(AuditActions.CaseUpdate, "CASE", (long)c.Id, (long)c.Id,
            details: new { Action = "COMPLETE_HEARING", HearingPublicId = hearing.PublicId, Outcome = outcome });
        await events.PublishAsync(new HearingCompleted(c.PublicId, hearing.PublicId, outcome!, next?.PublicId), ct);
        if (next is not null)
        {
            await events.PublishAsync(new HearingScheduled(c.PublicId, next.PublicId, next.ScheduledAt), ct);
        }

        return new CompleteHearingResponse(await writer.ViewAsync(hearing, ct), next is null ? null : await writer.ViewAsync(next, ct), warnings);
    }
}
