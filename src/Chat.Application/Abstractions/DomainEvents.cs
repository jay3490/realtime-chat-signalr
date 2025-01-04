using System.Collections.Concurrent;
using Chat.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Chat.Application.Abstractions;

public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls every registered handler for each event. Events are dispatched after the
/// transaction has committed, so a failing handler is logged rather than surfaced: the
/// state change it reacts to has already happened and cannot be rolled back.
/// </summary>
internal sealed partial class DomainEventDispatcher(
    IServiceProvider services,
    ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    private static readonly ConcurrentDictionary<Type, EventInvoker> Invokers = new();

    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var invoker = Invokers.GetOrAdd(
                domainEvent.GetType(),
                type => (EventInvoker)Activator.CreateInstance(typeof(EventInvoker<>).MakeGenericType(type))!);

            foreach (var (handlerName, invocation) in invoker.Resolve(domainEvent, services, cancellationToken))
            {
                try
                {
                    await invocation;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogHandlerFailed(logger, ex, handlerName, domainEvent.GetType().Name);
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Domain event handler {Handler} failed for {Event}")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string handler, string @event);

    private abstract class EventInvoker
    {
        public abstract IEnumerable<(string Handler, Task Invocation)> Resolve(
            IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class EventInvoker<TEvent> : EventInvoker
        where TEvent : IDomainEvent
    {
        public override IEnumerable<(string Handler, Task Invocation)> Resolve(
            IDomainEvent domainEvent, IServiceProvider services, CancellationToken cancellationToken)
        {
            foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
            {
                yield return (handler.GetType().Name, InvokeSafely(handler, (TEvent)domainEvent, cancellationToken));
            }
        }

        // Wraps the call so a handler that throws synchronously is reported the same way
        // as one whose task faults.
        private static async Task InvokeSafely(
            IDomainEventHandler<TEvent> handler, TEvent domainEvent, CancellationToken cancellationToken) =>
            await handler.HandleAsync(domainEvent, cancellationToken);
    }
}
