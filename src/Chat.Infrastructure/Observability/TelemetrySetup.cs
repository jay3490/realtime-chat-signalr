using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Chat.Infrastructure.Observability;

public static class TelemetrySetup
{
    // Instruments ASP.NET Core 8 publishes by itself: request durations, Kestrel connections
    // and SignalR connections (signalr.server.active_connections, connection durations).
    private static readonly string[] FrameworkMeters =
    [
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.AspNetCore.Http.Connections",
    ];

    /// <summary>OpenTelemetry metrics for this service, exported for Prometheus to scrape.</summary>
    public static IServiceCollection AddChatTelemetry(this IServiceCollection services, string serviceName)
    {
        services.AddSingleton<ChatMetrics>();
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics => metrics
                .AddMeter(ChatMetrics.MeterName)
                .AddMeter(FrameworkMeters)
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());
        return services;
    }

    /// <summary>Serves the Prometheus text format at <c>/metrics</c>.</summary>
    public static IEndpointRouteBuilder MapChatMetrics(this IEndpointRouteBuilder app)
    {
        app.MapPrometheusScrapingEndpoint();
        return app;
    }
}
