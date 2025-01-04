using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.Application.Abstractions;

/// <summary>
/// A small replacement for a mediator library. Handlers are resolved from the current
/// scope; the generic plumbing needed to call them is built once per request type and cached.
/// </summary>
internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var invoker = (Invoker<TResult>)Invokers.GetOrAdd(
            command.GetType(),
            type => Activator.CreateInstance(typeof(CommandInvoker<,>).MakeGenericType(type, typeof(TResult)))!);
        return invoker.InvokeAsync(command, services, cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invoker = (Invoker<TResult>)Invokers.GetOrAdd(
            query.GetType(),
            type => Activator.CreateInstance(typeof(QueryInvoker<,>).MakeGenericType(type, typeof(TResult)))!);
        return invoker.InvokeAsync(query, services, cancellationToken);
    }

    private abstract class Invoker<TResult>
    {
        public abstract Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class CommandInvoker<TCommand, TResult> : Invoker<TResult>
        where TCommand : ICommand<TResult>
    {
        public override Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken) =>
            services.GetRequiredService<ICommandHandler<TCommand, TResult>>().HandleAsync((TCommand)request, cancellationToken);
    }

    private sealed class QueryInvoker<TQuery, TResult> : Invoker<TResult>
        where TQuery : IQuery<TResult>
    {
        public override Task<TResult> InvokeAsync(object request, IServiceProvider services, CancellationToken cancellationToken) =>
            services.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync((TQuery)request, cancellationToken);
    }
}
