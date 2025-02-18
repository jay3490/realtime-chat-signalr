using System.Security.Claims;
using Chat.Api.Auth;
using Chat.Application.Abstractions;
using Chat.Application.Messages;
using Chat.Application.Messages.Queries;
using Chat.Application.Realtime;
using Chat.Application.Rooms;
using Chat.Application.Rooms.Queries;
using Chat.Application.Users.Queries;

namespace Chat.Api.Endpoints;

/// <summary>
/// HTTP surface for loading state. Real-time traffic goes through the hub, but sending and
/// marking read are also available here; both paths run the same commands, so connected
/// clients are notified either way.
/// </summary>
internal static class ChatEndpoints
{
    public sealed record CreateChannelRequest(string Name);

    public sealed record OpenDirectRequest(string UserName);

    public sealed record SendMessageRequest(string Body);

    public sealed record EditMessageRequest(string Body);

    public sealed record MarkReadRequest(long MessageId);

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/rooms", (ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.QueryAsync(new GetMyRoomsQuery(user.GetUserName()), ct));

        api.MapGet("/rooms/unread", (ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.QueryAsync(new GetUnreadCountsQuery(user.GetUserName()), ct));

        api.MapGet("/rooms/{roomId:guid}", (Guid roomId, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.QueryAsync(new GetRoomSummaryQuery(roomId, user.GetUserName()), ct));

        api.MapGet("/rooms/{roomId:guid}/messages", (
                Guid roomId, long? before, long? after, int? limit,
                ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.QueryAsync(
                new GetMessageHistoryQuery(roomId, user.GetUserName(), before, after, limit ?? GetMessageHistoryQuery.DefaultLimit),
                ct));

        api.MapPost("/rooms/{roomId:guid}/messages", (
                Guid roomId, SendMessageRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.SendAsync(new SendMessageCommand(roomId, user.GetUserName(), request.Body), ct));

        api.MapPut("/rooms/{roomId:guid}/messages/{messageId:long}", (
                Guid roomId, long messageId, EditMessageRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.SendAsync(new EditMessageCommand(roomId, messageId, user.GetUserName(), request.Body), ct));

        api.MapDelete("/rooms/{roomId:guid}/messages/{messageId:long}", async (
                Guid roomId, long messageId, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            {
                await dispatcher.SendAsync(new DeleteMessageCommand(roomId, messageId, user.GetUserName()), ct);
                return Results.NoContent();
            });

        api.MapGet("/rooms/{roomId:guid}/read-markers", (Guid roomId, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.QueryAsync(new GetReadMarkersQuery(roomId, user.GetUserName()), ct));

        api.MapPost("/rooms/{roomId:guid}/read", async (
                Guid roomId, MarkReadRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            {
                await dispatcher.SendAsync(new MarkReadCommand(roomId, user.GetUserName(), request.MessageId), ct);
                return Results.NoContent();
            });

        api.MapGet("/channels", (ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.QueryAsync(new GetChannelsQuery(user.GetUserName()), ct));

        api.MapPost("/channels", async (CreateChannelRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var userName = user.GetUserName();
            var roomId = await dispatcher.SendAsync(new CreateChannelCommand(request.Name, userName), ct);
            var room = await dispatcher.QueryAsync(new GetRoomSummaryQuery(roomId, userName), ct);
            return Results.Created($"/api/rooms/{roomId}", room);
        });

        api.MapPost("/channels/{roomId:guid}/join", async (Guid roomId, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var userName = user.GetUserName();
            await dispatcher.SendAsync(new JoinChannelCommand(roomId, userName), ct);
            return await dispatcher.QueryAsync(new GetRoomSummaryQuery(roomId, userName), ct);
        });

        api.MapPost("/direct", async (OpenDirectRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken ct) =>
        {
            var userName = user.GetUserName();
            var roomId = await dispatcher.SendAsync(new OpenDirectConversationCommand(userName, request.UserName), ct);
            return await dispatcher.QueryAsync(new GetRoomSummaryQuery(roomId, userName), ct);
        });

        api.MapGet("/users", (IDispatcher dispatcher, CancellationToken ct) =>
            dispatcher.QueryAsync(new GetUsersQuery(), ct));

        api.MapGet("/presence", (IPresenceTracker presence, CancellationToken ct) =>
            presence.GetOnlineUsersAsync(ct));

        return app;
    }
}
