using Chat.Application;
using Chat.Application.Abstractions;
using Chat.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chat.UnitTests.Application;

public class DispatcherTests
{
    [Fact]
    public async Task Commands_and_queries_reach_their_handlers()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        Assert.Equal(5, await dispatcher.SendAsync(new AddCommand(2, 3)));
        Assert.Equal("HELLO", await dispatcher.QueryAsync(new ShoutQuery("hello")));
    }

    [Fact]
    public async Task A_failing_event_handler_does_not_stop_the_others()
    {
        var log = new EventLog();
        await using var provider = BuildProvider(log);
        await using var scope = provider.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync([new SomethingHappened("first"), new SomethingHappened("second")]);

        Assert.Equal(["first", "second"], log.Seen);
    }

    private static ServiceProvider BuildProvider(EventLog? log = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log ?? new EventLog());
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddChatApplication(typeof(DispatcherTests).Assembly);
        return services.BuildServiceProvider(validateScopes: true);
    }

    internal sealed record AddCommand(int Left, int Right) : ICommand<int>;

    internal sealed record ShoutQuery(string Text) : IQuery<string>;

    internal sealed record SomethingHappened(string Name) : IDomainEvent;

    internal sealed class EventLog
    {
        public List<string> Seen { get; } = [];
    }

    internal sealed class AddHandler : ICommandHandler<AddCommand, int>
    {
        public Task<int> HandleAsync(AddCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(command.Left + command.Right);
    }

    internal sealed class ShoutHandler : IQueryHandler<ShoutQuery, string>
    {
        public Task<string> HandleAsync(ShoutQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(query.Text.ToUpperInvariant());
    }

    internal sealed class ThrowingHandler : IDomainEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }

    internal sealed class RecordingHandler(EventLog log) : IDomainEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken cancellationToken)
        {
            log.Seen.Add(domainEvent.Name);
            return Task.CompletedTask;
        }
    }
}
