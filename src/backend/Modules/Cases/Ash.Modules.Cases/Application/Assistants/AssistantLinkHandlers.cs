using Ash.Modules.Audit.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Cases.Infrastructure;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Events;
using Ash.SharedKernel.Identity;
using Ash.SharedKernel.Results;
using Ash.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Ash.Modules.Cases.Application.Assistants;

/// <summary>A lawyer an assistant is linked to, with how many cases the assistant holds through the link.</summary>
public sealed record AssistantLinkView(Guid LinkPublicId, Guid LawyerPublicId, string LawyerName, int Cases, DateTime Since);

public sealed record OfficeAssistantView(Guid PublicId, string Name, IReadOnlyList<AssistantLinkView> Lawyers);

public sealed record OfficeLawyerView(Guid PublicId, string Name);

/// <summary>"Ndihmësit ligjorë" of the Kryetar's office: every assistant with their lawyers, and the lawyers to pick.</summary>
public sealed record AssistantLinksResponse(IReadOnlyList<OfficeAssistantView> Assistants, IReadOnlyList<OfficeLawyerView> Lawyers);

public sealed record CreateAssistantLinkRequest(Guid? AssistantUserPublicId, Guid? LawyerUserPublicId);

/// <summary>
/// "Ndihmësit ligjorë" (task 053e, client decision C, default D4): the Kryetar (assistant.manage, now a SYSTEM
/// permission of OFFICE_HEAD — no longer the primary lawyer's per case) attaches an active LEGAL_ASSISTANT of the office to
/// active STATE_LAWYERs of the office, and detaches them. <see cref="AssistantSync"/> then gives / takes the cases. The
/// assistant is told once per link (AssistantLinked / AssistantUnlinked).
/// </summary>
internal sealed class AssistantLinkHandlers(
    CasesDbContext db,
    AssistantSync sync,
    IUserDirectory users,
    IAuditLog audit,
    IEventPublisher events,
    ICurrentUser user,
    IClock clock)
{
    public const string Permission = "assistant.manage";
    public const string AssistantRole = "LEGAL_ASSISTANT";
    public const string LawyerRole = "STATE_LAWYER";

    public async Task<Result<AssistantLinksResponse>> ListAsync(CancellationToken ct)
    {
        if (user.OfficeId is not { } officeId)
        {
            return Forbidden();
        }

        var assistants = await users.GetOfficeUsersByRoleAsync(officeId, AssistantRole, ct);
        var lawyers = await users.GetOfficeUsersByRoleAsync(officeId, LawyerRole, ct);
        var office = (ushort)officeId;
        var links = await db.AssistantLinks.AsNoTracking().Where(l => l.OfficeId == office && l.EndedAt == null).ToListAsync(ct);
        var linkIds = links.Select(l => (ulong?)l.Id).ToList();
        var counts = await db.CaseAssignments.AsNoTracking()
            .Where(a => linkIds.Contains(a.AssistantLinkId) && a.EndedAt == null)
            .GroupBy(a => a.AssistantLinkId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var refs = await users.GetRefsAsync(links.Select(l => (long)l.LawyerUserId).Distinct().ToList(), ct);

        var views = assistants.OrderBy(a => a.FullName).Select(a => new OfficeAssistantView(a.PublicId, a.FullName,
                links.Where(l => l.AssistantUserId == a.Id && refs.ContainsKey(l.LawyerUserId))
                    .Select(l => new AssistantLinkView(l.PublicId, refs[l.LawyerUserId].PublicId, refs[l.LawyerUserId].Name,
                        counts.GetValueOrDefault(l.Id), l.CreatedAt))
                    .OrderBy(l => l.LawyerName)
                    .ToList()))
            .ToList();
        return new AssistantLinksResponse(views,
            lawyers.OrderBy(l => l.FullName).Select(l => new OfficeLawyerView(l.PublicId, l.FullName)).ToList());
    }

    public async Task<Result<AssistantLinkView>> CreateAsync(CreateAssistantLinkRequest req, CancellationToken ct)
    {
        if (user.OfficeId is not { } officeId)
        {
            return Forbidden();
        }

        var errors = new FieldErrors();
        var assistant = (await users.GetOfficeUsersByRoleAsync(officeId, AssistantRole, ct)).FirstOrDefault(u => u.PublicId == req.AssistantUserPublicId);
        if (assistant is null)
        {
            errors.Add("assistantUserPublicId", "Zgjidhni një ndihmës ligjor aktiv të zyrës.");
        }

        var lawyer = (await users.GetOfficeUsersByRoleAsync(officeId, LawyerRole, ct)).FirstOrDefault(u => u.PublicId == req.LawyerUserPublicId);
        if (lawyer is null)
        {
            errors.Add("lawyerUserPublicId", "Zgjidhni një avokat aktiv të zyrës.");
        }

        if (errors.Any)
        {
            return errors.ToError("Lidhja nuk është e vlefshme.");
        }

        var link = new AssistantLink
        {
            PublicId = Guid.NewGuid(),
            AssistantUserId = (uint)assistant!.Id,
            LawyerUserId = (uint)lawyer!.Id,
            OfficeId = (ushort)officeId,
            CreatedBy = (uint)user.UserId,
            CreatedAt = clock.UtcNow,
        };
        if (await db.AssistantLinks.AnyAsync(l => l.AssistantUserId == link.AssistantUserId && l.LawyerUserId == link.LawyerUserId && l.EndedAt == null, ct))
        {
            return AlreadyLinked();
        }

        db.AssistantLinks.Add(link);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("uq_al_active_pair", StringComparison.Ordinal) == true)
        {
            return AlreadyLinked(); // a parallel request was first
        }

        var cases = await sync.SyncLinkAsync(link, ct);
        audit.Record(AuditActions.AdminChange, "ASSISTANT_LINK", (long)link.Id,
            details: new { Action = "ASSISTANT_LINKED", Assistant = assistant.FullName, Lawyer = lawyer.FullName, Cases = cases });
        await events.PublishAsync(new AssistantLinked(assistant.PublicId, lawyer.FullName, cases), ct);
        return new AssistantLinkView(link.PublicId, lawyer.PublicId, lawyer.FullName, cases, link.CreatedAt);
    }

    public async Task<Result<bool>> EndAsync(Guid linkPublicId, CancellationToken ct)
    {
        if (user.OfficeId is not { } officeId)
        {
            return Forbidden();
        }

        var link = await db.AssistantLinks.SingleOrDefaultAsync(l => l.PublicId == linkPublicId && l.OfficeId == (ushort)officeId, ct);
        if (link is null)
        {
            return Error.NotFound("assistant_link.not_found", "Lidhja nuk u gjet.");
        }

        if (link.EndedAt is not null)
        {
            return Error.Conflict("assistant_link.ended", "Kjo lidhje është hequr tashmë.");
        }

        link.EndedAt = clock.UtcNow;
        link.EndedBy = (uint)user.UserId;
        await db.SaveChangesAsync(ct);
        await sync.SyncLinkAsync(link, ct);

        var refs = await users.GetRefsAsync([link.AssistantUserId, link.LawyerUserId], ct);
        audit.Record(AuditActions.AdminChange, "ASSISTANT_LINK", (long)link.Id,
            details: new { Action = "ASSISTANT_UNLINKED", Assistant = refs[link.AssistantUserId].Name, Lawyer = refs[link.LawyerUserId].Name });
        await events.PublishAsync(new AssistantUnlinked(refs[link.AssistantUserId].PublicId, refs[link.LawyerUserId].Name), ct);
        return true;
    }

    private static Error Forbidden() => Error.Forbidden("assistant_link.forbidden", "Nuk keni leje për këtë veprim.");

    private static Error AlreadyLinked() =>
        Error.Conflict("assistant_link.exists", "Ky ndihmës është i lidhur tashmë me këtë avokat.");
}
