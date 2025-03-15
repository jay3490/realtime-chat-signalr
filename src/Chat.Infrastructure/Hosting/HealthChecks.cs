using System.Text.Json;
using Chat.Infrastructure.Persistence;
using Chat.Infrastructure.ReadModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Chat.Infrastructure.Hosting;

/// <summary>Healthy while the database behind <typeparamref name="TContext"/> accepts connections.</summary>
public sealed class DatabaseHealthCheck<TContext>(TContext db) : IHealthCheck
    where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Cannot connect to the database.");
}

/// <summary>
/// Healthy while Redis answers a PING. Redis carries the SignalR backplane and presence, so
/// an instance that cannot reach it would accept connections whose messages never cross to
/// other instances; taking it out of rotation is the safer outcome.
/// </summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"PING {latency.TotalMilliseconds:0.0} ms");
        }
        catch (RedisException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis did not answer.", ex);
        }
    }
}

public static class HealthEndpoints
{
    /// <summary>Checks tagged with this decide readiness; liveness runs no checks.</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registers the readiness checks that apply to this host: the databases it uses, Redis
    /// when configured, and whether the host is draining for shutdown.
    /// </summary>
    public static IHealthChecksBuilder AddChatHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks()
            .AddCheck<DrainingHealthCheck>("draining", tags: [ReadyTag]);

        if (services.Any(s => s.ServiceType == typeof(ChatDbContext)))
        {
            checks.AddCheck<DatabaseHealthCheck<ChatDbContext>>("write-database", tags: [ReadyTag]);
        }

        if (services.Any(s => s.ServiceType == typeof(ReadModelDbContext)))
        {
            checks.AddCheck<DatabaseHealthCheck<ReadModelDbContext>>("read-database", tags: [ReadyTag]);
        }

        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString("Redis")))
        {
            checks.AddCheck<RedisHealthCheck>("redis", tags: [ReadyTag]);
        }

        return checks;
    }

    /// <summary>
    /// <c>/health/live</c>: the process is up (no dependencies checked, so a database outage
    /// does not get every pod restarted). <c>/health/ready</c>: dependencies are reachable and
    /// the host is not shutting down. <c>/health</c> stays as an alias of liveness.
    /// </summary>
    public static IEndpointRouteBuilder MapChatHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var live = new HealthCheckOptions { Predicate = _ => false };
        app.MapHealthChecks("/health", live);
        app.MapHealthChecks("/health/live", live);
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteReportAsync,
        });
        return app;
    }

    private static Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), description = e.Value.Description }),
        });
    }
}
