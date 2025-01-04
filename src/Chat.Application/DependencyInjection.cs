using System.Reflection;
using Chat.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chat.Application;

public static class DependencyInjection
{
    private static readonly Type[] HandlerInterfaces =
    [
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(IDomainEventHandler<>),
    ];

    /// <summary>
    /// Registers the dispatchers and every command, query and domain event handler in this
    /// assembly plus any <paramref name="handlerAssemblies"/> (for example the API, which
    /// owns the handlers that push events to connected clients).
    /// </summary>
    public static IServiceCollection AddChatApplication(
        this IServiceCollection services, params Assembly[] handlerAssemblies)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        foreach (var assembly in handlerAssemblies.Prepend(typeof(DependencyInjection).Assembly).Distinct())
        {
            services.AddHandlersFrom(assembly);
        }

        return services;
    }

    private static void AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        var implementations = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var implementation in implementations)
        {
            var handlerInterfaces = implementation.GetInterfaces()
                .Where(i => i.IsGenericType && HandlerInterfaces.Contains(i.GetGenericTypeDefinition()));

            foreach (var handlerInterface in handlerInterfaces)
            {
                services.TryAddEnumerable(ServiceDescriptor.Scoped(handlerInterface, implementation));
            }
        }
    }
}
