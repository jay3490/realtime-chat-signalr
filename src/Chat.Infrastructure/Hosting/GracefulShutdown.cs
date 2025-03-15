using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Hosting;

public sealed class ShutdownOptions
{
    public const string SectionName = "Shutdown";

    /// <summary>
    /// How long to keep serving after shutdown starts while reporting not-ready, so the load
    /// balancer stops sending new connections before the server stops accepting them.
    /// </summary>
    public int DrainSeconds { get; set; }

    /// <summary>Upper bound for the whole shutdown, drain included.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Whether the host has begun shutting down.</summary>
public sealed class ShutdownState
{
    private volatile bool _draining;

    public bool IsDraining => _draining;

    public void BeginDraining() => _draining = true;
}

/// <summary>Unhealthy once the host is draining, which takes it out of readiness.</summary>
public sealed class DrainingHealthCheck(ShutdownState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsDraining
            ? new HealthCheckResult(context.Registration.FailureStatus, "Shutting down.")
            : HealthCheckResult.Healthy());
}

/// <summary>
/// Runs before any hosted service stops: marks the host as draining and waits
/// <see cref="ShutdownOptions.DrainSeconds"/>. Only then do Kestrel (which closes WebSockets,
/// so SignalR clients reconnect to another instance), the outbox relay and the consumers stop.
/// </summary>
public sealed partial class DrainOnShutdownService(
    ShutdownState state,
    IOptions<ShutdownOptions> options,
    TimeProvider clock,
    ILogger<DrainOnShutdownService> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        state.BeginDraining();
        var drain = TimeSpan.FromSeconds(options.Value.DrainSeconds);
        if (drain <= TimeSpan.Zero)
        {
            return;
        }

        LogDraining(logger, drain.TotalSeconds);
        try
        {
            await Task.Delay(drain, clock, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The shutdown timeout ran out first; carry on stopping.
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Shutting down: not ready, draining for {Seconds}s")]
    private static partial void LogDraining(ILogger logger, double seconds);
}

public static class GracefulShutdownSetup
{
    public static IServiceCollection AddGracefulShutdown(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ShutdownOptions.SectionName);
        services.Configure<ShutdownOptions>(section);
        var options = section.Get<ShutdownOptions>() ?? new ShutdownOptions();

        services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds));
        services.AddSingleton<ShutdownState>();
        services.AddSingleton<DrainingHealthCheck>();
        services.AddHostedService<DrainOnShutdownService>();
        return services;
    }
}
