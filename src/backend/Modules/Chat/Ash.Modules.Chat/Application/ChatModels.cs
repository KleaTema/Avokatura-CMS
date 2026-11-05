namespace Ash.Modules.Chat.Application;

/// <summary>A user in a chat: public id and "First Last" (never the internal id).</summary>
public sealed record ChatUser(Guid Id, string Name);

/// <summary>
/// One message as the API and the hub show it. A deleted message keeps its place: IsDeleted, Body = "Mesazh i fshirë"
/// (the real body stays in the database for the record). SYSTEM messages have no sender.
/// </summary>
public sealed record ChatMessage(
    Guid Id,
    string Type,
    ChatUser? Sender,
    string Body,
    bool IsDeleted,
    Guid? ReplyToMessageId,
    DateTime CreatedAt,
    DateTime? EditedAt);

/// <summary>A conversation in "my conversations": the case chat's title is the case code and the court.</summary>
public sealed record ChatConversation(
    Guid Id,
    string Type,
    Guid? CasePublicId,
    string Title,
    bool IsReadOnly,
    DateTime? LastMessageAt,
    string? LastMessagePreview,
    /// <summary>At most 99; <see cref="UnreadCapped"/> = more (the UI shows "99+").</summary>
    int Unread,
    bool UnreadCapped);

public sealed record ConversationPage(IReadOnlyList<ChatConversation> Items, string? NextCursor);

/// <summary>Newest first; <see cref="NextCursor"/> (opaque) gives the older ones.</summary>
public sealed record MessagePage(IReadOnlyList<ChatMessage> Items, string? NextCursor);

public sealed record UnreadTotal(int Unread, bool Capped);

public sealed record SendMessageRequest(string? Body, Guid? ReplyToMessageId);

public sealed record EditMessageRequest(string? Body);

public sealed record MarkReadRequest(Guid LastMessageId);

/// <summary>POST /api/chat/direct — refused under the case-only policy.</summary>
public sealed record OpenDirectRequest(Guid UserId);

public static class ChatTexts
{
    public const string Deleted = "Mesazh i fshirë";
    public static string Joined(string name) => $"{name} u shtua në bisedë";
    public static string Left(string name) => $"{name} doli nga biseda";
}
