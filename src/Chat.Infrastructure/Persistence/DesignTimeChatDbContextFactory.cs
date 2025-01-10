using Chat.Application.Abstractions;
using Chat.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chat.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef</c> to build the model when adding migrations. No connection is
/// opened, so the connection string only needs to be well formed.
/// </summary>
internal sealed class DesignTimeChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql("Host=localhost;Database=chat;Username=chat;Password=chat")
            .Options;

        return new ChatDbContext(options, new NoDispatch());
    }

    private sealed class NoDispatch : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
