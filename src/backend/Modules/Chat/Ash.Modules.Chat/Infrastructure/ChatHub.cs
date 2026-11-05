using System.Collections.Concurrent;
using Ash.Modules.Chat.Application;
using Ash.Modules.Identity.Contracts;
using Ash.SharedKernel.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ash.Modules.Chat.Infrastructure;

/// <summary>
/// /hubs/chat (task 049): server → client only. Authenticated like /hubs/notifications (task 040: bearer token, or the
/// dev user, from the query string for /hubs/* only, masked in the logs). On connect the user joins one group per
/// conversation they are an ACTIVE participant of (checked in the database, not taken from the client); a participant
/// who leaves is taken out of the group at once (<see cref="ChatPush.LeaveAsync"/>). Events are sent only after the chat
/// transaction commits. TODO(akshi-q-signalr-backplane): groups and the connection map live in this process.
/// </summary>
public sealed class ChatHub(ChatDbContext db, IUserDirectory users, ChatConnections connections) : Hub
{
    public const string Path = "/hubs/chat";

    public const string MessageNew = "message.new";
    public const string MessageEdited = "message.edited";
    public const string MessageDeleted = "message.deleted";
    public const string ConversationRead = "conversation.read";
    public const string ParticipantsChanged = "participants.changed";

    public static string Group(Guid conversationPublicId) => $"conv:{conversationPublicId}";

    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var publicId))
        {
            Context.Abort();
            return;
        }

        var ids = await users.GetIdsAsync([publicId], Context.ConnectionAborted);
        if (!ids.TryGetValue(publicId, out var userId))
        {
            Context.Abort();
            return;
        }

        connections.Add(publicId, Context.ConnectionId);
        var uid = (uint)userId;
        var conversations = await db.Participants.AsNoTracking()
            .Where(p => p.UserId == uid && p.LeftAt == null)
            .Join(db.Conversations, p => p.ConversationId, c => c.Id, (p, c) => c.PublicId)
            .ToListAsync(Context.ConnectionAborted);
        foreach (var conversation in conversations)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(conversation), Context.ConnectionAborted);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Guid.TryParse(Context.UserIdentifier, out var publicId))
        {
            connections.Remove(publicId, Context.ConnectionId);
        }

        return Task.CompletedTask;
    }
}

/// <summary>The open chat connections of each user (public id), so a participant change can move them in or out of a group.</summary>
public sealed class ChatConnections
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _byUser = new();

    public void Add(Guid user, string connectionId) =>
        _byUser.GetOrAdd(user, _ => new ConcurrentDictionary<string, byte>())[connectionId] = 0;

    public void Remove(Guid user, string connectionId)
    {
        if (_byUser.TryGetValue(user, out var set))
        {
            set.TryRemove(connectionId, out _);
        }
    }

    public IReadOnlyList<string> Of(Guid user) =>
        _byUser.TryGetValue(user, out var set) ? set.Keys.ToList() : [];
}

/// <summary>
/// Sends chat events. Called ONLY after the chat transaction has committed (ChatUnitOfWork's OnCommitted), so a client
/// never hears of a message that could still roll back. Group changes are awaited (a removed participant must stop
/// receiving at once); the event sends are fire-and-forget like the notification push — the API is the truth.
/// </summary>
public sealed class ChatPush(IHubContext<ChatHub> hub, ChatConnections connections, ILogger<ChatPush> logger)
{
    public void Send(Guid conversation, string method, object payload) => Run(() =>
        hub.Clients.Group(ChatHub.Group(conversation)).SendAsync(method, payload));

    /// <summary>The users' open connections join the conversation's group, then everyone hears of the change.</summary>
    public async Task JoinAsync(Guid conversation, IReadOnlyCollection<Guid> userPublicIds)
    {
        foreach (var connection in userPublicIds.SelectMany(connections.Of))
        {
            await Guard(() => hub.Groups.AddToGroupAsync(connection, ChatHub.Group(conversation)));
        }
    }

    /// <summary>The users' open connections leave the group at once: they hear nothing more from it.</summary>
    public async Task LeaveAsync(Guid conversation, IReadOnlyCollection<Guid> userPublicIds)
    {
        foreach (var connection in userPublicIds.SelectMany(connections.Of))
        {
            await Guard(() => hub.Groups.RemoveFromGroupAsync(connection, ChatHub.Group(conversation)));
        }
    }

    private async Task Guard(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A chat group change failed; the user's next connect reads the participants again.");
        }
    }

    private void Run(Func<Task> push) => _ = Task.Run(async () =>
    {
        try
        {
            await push();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A chat push failed; the client sees the change on its next load.");
        }
    });
}

/// <summary>Payloads of the hub events (public ids only).</summary>
public sealed record ConversationReadPushed(Guid ConversationId, Guid UserId, Guid LastReadMessageId);

public sealed record ParticipantsChangedPushed(Guid ConversationId);

public sealed record MessagePushed(Guid ConversationId, ChatMessage Message);
