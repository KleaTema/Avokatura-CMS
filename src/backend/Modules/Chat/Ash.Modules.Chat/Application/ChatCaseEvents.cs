using Ash.Infrastructure.Persistence;
using Ash.Modules.Cases.Contracts;
using Ash.Modules.Cases.Contracts.Events;
using Ash.SharedKernel.Events;

namespace Ash.Modules.Chat.Application;

/// <summary>
/// Case events that change who is on a case (task 049). The handler runs INSIDE the core transaction but changes
/// nothing there: it only asks for a reconcile after the core commit (IUnitOfWork.OnCommittedAsync), so a rolled-back
/// assignment never touches the chat, and the chat reads the committed team. An archived dossier makes the chats of
/// all its levels read-only. A CLOSED case keeps a writable chat (task 020).
/// </summary>
internal sealed class ChatCaseEvents(IUnitOfWork core, ICaseLookup cases, ChatParticipants participants) :
    IEventHandler<LawyerAssigned>,
    IEventHandler<LawyerTransferred>,
    IEventHandler<AssistantAssigned>,
    IEventHandler<AssistantRemoved>,
    IEventHandler<JuristAssigned>,
    IEventHandler<InstitutionLinked>,
    IEventHandler<InstitutionUnlinked>,
    IEventHandler<CaseDelegated>,
    IEventHandler<CaseUndelegated>,
    IEventHandler<DossierArchived>
{
    public Task HandleAsync(LawyerAssigned e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);
    public Task HandleAsync(LawyerTransferred e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);
    public Task HandleAsync(AssistantAssigned e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);
    public Task HandleAsync(AssistantRemoved e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);
    public Task HandleAsync(JuristAssigned e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);
    public Task HandleAsync(InstitutionLinked e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);
    public Task HandleAsync(InstitutionUnlinked e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);
    public Task HandleAsync(CaseDelegated e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);
    public Task HandleAsync(CaseUndelegated e, CancellationToken ct = default) => AfterCommit(e.CasePublicId, ct);

    public async Task HandleAsync(DossierArchived e, CancellationToken ct = default)
    {
        foreach (var level in e.CasePublicIds)
        {
            await AfterCommit(level, ct);
        }
    }

    private async Task AfterCommit(Guid casePublicId, CancellationToken ct)
    {
        if (await cases.GetAsync(casePublicId, ct) is { } c)
        {
            core.OnCommittedAsync(() => participants.ReconcileQuietlyAsync(c.Id));
        }
    }
}
