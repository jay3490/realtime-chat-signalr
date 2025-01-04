namespace Chat.Application.Abstractions;

/// <summary>A request that changes state. Handled by exactly one <see cref="ICommandHandler{TCommand,TResult}"/>.</summary>
public interface ICommand<TResult>;

/// <summary>A request that only reads state. Handled by exactly one <see cref="IQueryHandler{TQuery,TResult}"/>.</summary>
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Routes commands and queries to their handlers. Keeps hubs and endpoints unaware of
/// which class implements a use case.
/// </summary>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
