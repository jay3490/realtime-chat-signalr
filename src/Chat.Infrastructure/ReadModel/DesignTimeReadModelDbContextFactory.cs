using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Chat.Infrastructure.ReadModel;

/// <summary>
/// Used by <c>dotnet ef</c> to build the read model when adding its migrations, which target
/// PostgreSQL and live in the <c>read</c> schema. No connection is opened.
/// </summary>
internal sealed class DesignTimeReadModelDbContextFactory : IDesignTimeDbContextFactory<ReadModelDbContext>
{
    public ReadModelDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReadModelDbContext>()
            .UseNpgsql("Host=localhost;Database=chat;Username=chat;Password=chat", ReadModelSetup.ConfigureNpgsql)
            .Options;

        return new ReadModelDbContext(options);
    }
}
