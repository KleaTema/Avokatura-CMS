using Ash.AspNetCore.Http;
using Ash.Modules.Chat.Application;
using Ash.Modules.Chat.Infrastructure;
using Ash.SharedKernel.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ash.Modules.Chat.Endpoints;

/// <summary>Task 049: /api/chat and the hub /hubs/chat. Every route is for a signed-in user; who may see what is decided by the participant rows.</summary>
public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChat(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/chat").RequireAuthorization();

        group.MapGet("/conversations", async (string? after, int? limit, ChatService chat, CancellationToken ct) =>
                Results.Ok(await chat.ListAsync(after, limit, ct)))
            .Produces<ConversationPage>()
            .WithName("ListChatConversations");

        group.MapPost("/cases/{caseId:guid}/conversation", async (Guid caseId, ChatService chat, CancellationToken ct) =>
                ToResult(await chat.OpenCaseAsync(caseId, ct)))
            .Produces<ChatConversation>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("OpenCaseChat");

        group.MapPost("/direct", async (OpenDirectRequest req, ChatService chat, CancellationToken ct) =>
                ToResult(await chat.OpenDirectAsync(req, ct)))
            .Produces<ChatConversation>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .WithName("OpenDirectChat");

        group.MapGet("/conversations/{id:guid}/messages", async (Guid id, string? before, int? limit, ChatService chat, CancellationToken ct) =>
                ToResult(await chat.MessagesAsync(id, before, limit, ct)))
            .Produces<MessagePage>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("ListChatMessages");

        group.MapPost("/conversations/{id:guid}/messages", async (Guid id, SendMessageRequest req, ChatService chat, CancellationToken ct) =>
            {
                var result = await chat.SendAsync(id, req, ct);
                return result.IsSuccess ? Results.Created((string?)null, result.Value) : result.Error!.ToProblem();
            })
            .Produces<ChatMessage>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("SendChatMessage");

        group.MapPut("/messages/{id:guid}", async (Guid id, EditMessageRequest req, ChatService chat, CancellationToken ct) =>
                ToResult(await chat.EditAsync(id, req, ct)))
            .Produces<ChatMessage>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("EditChatMessage");

        group.MapDelete("/messages/{id:guid}", async (Guid id, ChatService chat, CancellationToken ct) =>
                ToResult(await chat.DeleteAsync(id, ct)))
            .Produces<ChatMessage>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithName("DeleteChatMessage");

        group.MapPost("/conversations/{id:guid}/read", async (Guid id, MarkReadRequest req, ChatService chat, CancellationToken ct) =>
            {
                var result = await chat.MarkReadAsync(id, req, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error!.ToProblem();
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithName("MarkChatRead");

        group.MapGet("/unread-total", async (ChatService chat, CancellationToken ct) => Results.Ok(await chat.UnreadTotalAsync(ct)))
            .Produces<UnreadTotal>()
            .WithName("ChatUnreadTotal");

        endpoints.MapHub<ChatHub>(ChatHub.Path).RequireAuthorization();
        return endpoints;
    }

    private static IResult ToResult<T>(Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : result.Error!.ToProblem();
}
