using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.Modules.Identity.Contracts;
using Ash.Modules.Notifications.Contracts;
using Ash.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Notifications.Application;

/// <summary>
/// The case events that become notifications (task 040). Every handler runs in the publishing request's transaction
/// (InProcessEventPublisher), so the rows commit or roll back with the action. The sender skips the actor and inactive
/// users; each handler makes sure one person gets one row for one event.
/// </summary>
internal sealed class CaseEventNotifications(
    INotificationSender sender,
    ICaseLookup cases,
    IUserDirectory users,
    IInstitutionDirectory institutions,
    CurrentActor actor,
    ILogger<CaseEventNotifications> logger) :
    IEventHandler<CaseRegistered>,
    IEventHandler<LawyerAssigned>,
    IEventHandler<LawyerTransferred>,
    IEventHandler<AssistantAssigned>,
    IEventHandler<AssistantRemoved>,
    IEventHandler<JuristAssigned>,
    IEventHandler<InstitutionLinked>,
    IEventHandler<CaseDelegated>,
    IEventHandler<CaseUndelegated>,
    IEventHandler<PreviousLevelNotClosed>,
    IEventHandler<CaseClosed>
{
    public const string InstitutionUserRole = "INSTITUTION_USER";
    public const string InstitutionJuristRole = "INSTITUTION_JURIST";
    private const string DelegatedMode = "DELEGATED";
    private const string RelatedMode = "RELATIM";

    /// <summary>Office head: the case waits for its lawyers.</summary>
    public async Task HandleAsync(CaseRegistered e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is { } c)
        {
            await sender.SendAsync(NotificationTexts.CaseRegistered(c), NotificationRecipients.Of(e.HeadUserPublicId), ct);
        }
    }

    /// <summary>The primary and the secondary lawyer; the INSTITUTION_USERs of the institutions linked to the case.</summary>
    public async Task HandleAsync(LawyerAssigned e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c)
        {
            return;
        }

        await sender.SendAsync(NotificationTexts.LawyerAssigned(c, NotificationTexts.PrimaryRole), NotificationRecipients.Of(e.PrimaryUserPublicId), ct);
        if (e.SecondaryUserPublicId is { } secondary && secondary != e.PrimaryUserPublicId)
        {
            await sender.SendAsync(NotificationTexts.LawyerAssigned(c, NotificationTexts.SecondaryRole), NotificationRecipients.Of(secondary), ct);
        }

        if (e.LinkedInstitutionPublicIds.Count == 0)
        {
            return;
        }

        var primaryName = (await NamesAsync([e.PrimaryUserPublicId], ct)).GetValueOrDefault(e.PrimaryUserPublicId) ?? string.Empty;
        var lawyers = new HashSet<Guid> { e.PrimaryUserPublicId };
        if (e.SecondaryUserPublicId is { } s)
        {
            lawyers.Add(s);
        }

        var recipients = new List<long>();
        foreach (var institution in await institutions.GetByPublicIdsAsync(e.LinkedInstitutionPublicIds, ct))
        {
            recipients.AddRange((await users.GetInstitutionUsersByRoleAsync(institution.Id, InstitutionUserRole, ct))
                .Where(u => !lawyers.Contains(u.PublicId)).Select(u => u.Id));
        }

        await sender.SendAsync(NotificationTexts.InstitutionCaseAssigned(c, primaryName), NotificationRecipients.OfIds(recipients.Distinct()), ct);
    }

    /// <summary>
    /// Whoever became the primary or the secondary is told so; whoever left a role (and did not take the other one) is
    /// told who took it. An unchanged lawyer gets nothing.
    /// </summary>
    public async Task HandleAsync(LawyerTransferred e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c)
        {
            return;
        }

        var primaryChanged = e.NewPrimaryUserPublicId != e.OldPrimaryUserPublicId;
        var secondaryChanged = e.NewSecondaryUserPublicId != e.OldSecondaryUserPublicId;
        var names = await NamesAsync([e.NewPrimaryUserPublicId, e.NewSecondaryUserPublicId], ct);
        var told = new HashSet<Guid>();

        async Task Tell(Guid user, Contracts.NotificationMessage message)
        {
            if (told.Add(user))
            {
                await sender.SendAsync(message, NotificationRecipients.Of(user), ct);
            }
        }

        if (primaryChanged)
        {
            await Tell(e.NewPrimaryUserPublicId, NotificationTexts.LawyerAssigned(c, NotificationTexts.PrimaryRole));
        }

        if (secondaryChanged && e.NewSecondaryUserPublicId is { } newSecondary)
        {
            await Tell(newSecondary, NotificationTexts.LawyerAssigned(c, NotificationTexts.SecondaryRole));
        }

        if (primaryChanged)
        {
            await Tell(e.OldPrimaryUserPublicId, NotificationTexts.LawyerRemoved(c, NotificationTexts.PrimaryRole,
                names.GetValueOrDefault(e.NewPrimaryUserPublicId)));
        }

        if (secondaryChanged && e.OldSecondaryUserPublicId is { } oldSecondary)
        {
            await Tell(oldSecondary, NotificationTexts.LawyerRemoved(c, NotificationTexts.SecondaryRole,
                e.NewSecondaryUserPublicId is { } n ? names.GetValueOrDefault(n) : null));
        }
    }

    public async Task HandleAsync(AssistantAssigned e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is { } c)
        {
            await sender.SendAsync(NotificationTexts.AssistantAssigned(c), NotificationRecipients.Of(e.AssistantUserPublicId), ct);
        }
    }

    public async Task HandleAsync(AssistantRemoved e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is { } c)
        {
            await sender.SendAsync(NotificationTexts.AssistantRemoved(c), NotificationRecipients.Of(e.AssistantUserPublicId), ct);
        }
    }

    /// <summary>The new jurist; the jurist they replace, if any.</summary>
    public async Task HandleAsync(JuristAssigned e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c)
        {
            return;
        }

        var institution = await InstitutionNameAsync(e.InstitutionPublicId, ct);
        await sender.SendAsync(NotificationTexts.JuristAssigned(c, institution), NotificationRecipients.Of(e.JuristUserPublicId), ct);
        if (e.PreviousJuristUserPublicId is { } previous && previous != e.JuristUserPublicId)
        {
            var name = (await NamesAsync([e.JuristUserPublicId], ct)).GetValueOrDefault(e.JuristUserPublicId) ?? string.Empty;
            await sender.SendAsync(NotificationTexts.JuristReplaced(c, institution, name), NotificationRecipients.Of(previous), ct);
        }
    }

    /// <summary>The institution's active INSTITUTION_USERs.</summary>
    public async Task HandleAsync(InstitutionLinked e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c
            || (await institutions.GetByPublicIdsAsync([e.InstitutionPublicId], ct)).FirstOrDefault() is not { } institution)
        {
            return;
        }

        var recipients = await users.GetInstitutionUsersByRoleAsync(institution.Id, InstitutionUserRole, ct);
        await sender.SendAsync(NotificationTexts.InstitutionLinked(c, institution.Name, e.Mode == DelegatedMode),
            NotificationRecipients.OfIds(recipients.Select(u => u.Id)), ct);
    }

    /// <summary>The institution's INSTITUTION_USERs and the jurist it assigned to the case.</summary>
    public async Task HandleAsync(CaseDelegated e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c
            || (await institutions.GetByPublicIdsAsync([e.InstitutionPublicId], ct)).FirstOrDefault() is not { } institution)
        {
            return;
        }

        await sender.SendAsync(NotificationTexts.CaseDelegated(c, institution.Name, e.Mode == RelatedMode),
            NotificationRecipients.OfIds(await InstitutionRecipientsAsync(c, institution.Id, ct)), ct);
    }

    public async Task HandleAsync(CaseUndelegated e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c
            || (await institutions.GetByPublicIdsAsync([e.InstitutionPublicId], ct)).FirstOrDefault() is not { } institution)
        {
            return;
        }

        await sender.SendAsync(NotificationTexts.CaseUndelegated(c, institution.Name),
            NotificationRecipients.OfIds(await InstitutionRecipientsAsync(c, institution.Id, ct)), ct);
    }

    /// <summary>The recipients task 018 chose: the primary lawyer (if any) and the office head; the new hearing date.</summary>
    public async Task HandleAsync(PreviousLevelNotClosed e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.PreviousCasePublicId, ct) is { } c)
        {
            await sender.SendAsync(NotificationTexts.PreviousLevelNotClosed(c, e.SummonsNumber, e.HearingAt),
                NotificationRecipients.Of(e.PrimaryLawyerPublicId, e.HeadUserPublicId), ct);
        }
    }

    /// <summary>The whole current team (lawyers, assistants, jurists) except the one who closed it.</summary>
    public async Task HandleAsync(CaseClosed e, CancellationToken ct = default)
    {
        if (await CaseAsync(e.CasePublicId, ct) is not { } c)
        {
            return;
        }

        var team = await cases.GetTeamAsync(c.Id, ct);
        string? closedBy = null;
        if (actor.UserId is { } actorId)
        {
            closedBy = (await users.GetNamesAsync([actorId], ct)).GetValueOrDefault(actorId);
        }

        await sender.SendAsync(NotificationTexts.CaseClosed(c, closedBy),
            NotificationRecipients.OfIds(team.Select(m => m.UserId).Distinct()), ct);
    }

    private async Task<CaseSummary?> CaseAsync(Guid casePublicId, CancellationToken ct)
    {
        var c = await cases.GetAsync(casePublicId, ct);
        if (c is null)
        {
            logger.LogWarning("Case {CaseId} of an event was not found; no notification.", casePublicId);
        }

        return c;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> publicIds, CancellationToken ct)
    {
        var ids = publicIds.OfType<Guid>().Distinct().ToList();
        return (await users.GetStatesAsync([], ids, ct)).ToDictionary(u => u.PublicId, u => u.Name);
    }

    private async Task<string> InstitutionNameAsync(Guid institutionPublicId, CancellationToken ct) =>
        (await institutions.GetByPublicIdsAsync([institutionPublicId], ct)).FirstOrDefault()?.Name ?? "Institucioni";

    /// <summary>The institution's active INSTITUTION_USERs and the case's jurist(s) of that institution.</summary>
    private async Task<IEnumerable<long>> InstitutionRecipientsAsync(CaseSummary c, long institutionId, CancellationToken ct)
    {
        var institutionUsers = await users.GetInstitutionUsersByRoleAsync(institutionId, InstitutionUserRole, ct);
        var jurists = (await cases.GetTeamAsync(c.Id, ct)).Where(m => m.Role == InstitutionJuristRole).Select(m => m.UserId).ToList();
        var refs = await users.GetRefsAsync(jurists, ct);
        return institutionUsers.Select(u => u.Id)
            .Concat(jurists.Where(j => refs.TryGetValue(j, out var r) && r.InstitutionId == institutionId))
            .Distinct();
    }
}
