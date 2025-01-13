using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Users.Queries;

/// <summary>Everyone who has signed in at least once, for starting direct conversations.</summary>
public sealed record GetUsersQuery : IQuery<IReadOnlyList<string>>;

internal sealed class GetUsersHandler(IChatReadDbContext read) : IQueryHandler<GetUsersQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> HandleAsync(GetUsersQuery query, CancellationToken cancellationToken) =>
        await read.Users.OrderBy(u => u.Name).Select(u => u.Name).ToListAsync(cancellationToken);
}
