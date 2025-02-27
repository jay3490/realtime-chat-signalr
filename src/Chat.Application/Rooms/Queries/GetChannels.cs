using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Chat.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Rooms.Queries;

public sealed record ChannelDto(Guid Id, string Name, int MemberCount, bool IsMember);

/// <summary>All public channels, for browsing and joining.</summary>
public sealed record GetChannelsQuery(string UserName) : IQuery<IReadOnlyList<ChannelDto>>;

internal sealed class GetChannelsHandler(IChatReadDbContext read)
    : IQueryHandler<GetChannelsQuery, IReadOnlyList<ChannelDto>>
{
    public async Task<IReadOnlyList<ChannelDto>> HandleAsync(GetChannelsQuery query, CancellationToken cancellationToken) =>
        await read.Rooms
            .Where(r => r.Kind == RoomKind.Channel)
            .OrderBy(r => r.Name)
            .Select(r => new ChannelDto(
                r.Id,
                r.Name!,
                r.MemberCount,
                read.Members.Any(m => m.RoomId == r.Id && m.UserName == query.UserName)))
            .ToListAsync(cancellationToken);
}
