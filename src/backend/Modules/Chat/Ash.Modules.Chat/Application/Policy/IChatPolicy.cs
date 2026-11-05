using Ash.Modules.Cases.Contracts;

namespace Ash.Modules.Chat.Application.Policy;

/// <summary>
/// Who may chat with whom (task 049) — the one pluggable rule, chosen by configuration Chat:Policy.
/// Client decision: chat ONLY inside a case (<see cref="CaseOnlyChatPolicy"/>, the default). <see cref="OpenChatPolicy"/>
/// exists for a later change and is off. TODO(client-q-chat): revisit if the client wants direct (1:1) chats.
/// </summary>
public interface IChatPolicy
{
    bool CanStartDirect(long userA, long userB);

    /// <summary>Who belongs in the case chat now (internal user ids).</summary>
    Task<IReadOnlyList<long>> CaseParticipantsAsync(long caseId, CancellationToken ct);
}

public static class ChatPolicies
{
    public const string Section = "Chat:Policy";
    public const string CaseOnly = "CaseOnly";
    public const string Open = "Open";
}

/// <summary>
/// The default: no direct chats; a case chat holds everyone whose case profile gives chat.case (the current team and
/// the linked institutions' users) — never a Kryetar or Monitor through their system permissions alone.
/// </summary>
public sealed class CaseOnlyChatPolicy(ICaseLookup cases) : IChatPolicy
{
    public bool CanStartDirect(long userA, long userB) => false;

    public Task<IReadOnlyList<long>> CaseParticipantsAsync(long caseId, CancellationToken ct) =>
        cases.GetChatParticipantIdsAsync(caseId, ct);
}

/// <summary>Everyone may also chat 1:1 with anyone (off: not what the client chose).</summary>
public sealed class OpenChatPolicy(ICaseLookup cases) : IChatPolicy
{
    public bool CanStartDirect(long userA, long userB) => userA != userB;

    public Task<IReadOnlyList<long>> CaseParticipantsAsync(long caseId, CancellationToken ct) =>
        cases.GetChatParticipantIdsAsync(caseId, ct);
}
