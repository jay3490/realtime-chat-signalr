using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.Domain.Users;

namespace Chat.Application.Persistence;

/// <summary>
/// The read path. Query handlers project straight to DTOs through these untracked
/// queryables; nothing read here can be saved. It may point at a read replica.
/// </summary>
public interface IChatReadDbContext
{
    IQueryable<Room> Rooms { get; }

    IQueryable<RoomMember> RoomMembers { get; }

    IQueryable<Message> Messages { get; }

    IQueryable<User> Users { get; }
}
