using Chat.Infrastructure.Hosting;
using Chat.Infrastructure.Persistence;
using Chat.UnitTests.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Chat.UnitTests.Hosting;

public class HostingTests
{
    [Fact]
    public async Task Stopping_marks_the_host_as_draining_and_waits_the_drain_period()
    {
        var clock = new FakeTimeProvider();
        var state = new ShutdownState();
        var service = new DrainOnShutdownService(
            state, Options.Create(new ShutdownOptions { DrainSeconds = 5 }), clock, NullLogger<DrainOnShutdownService>.Instance);

        var stopping = service.StoppingAsync(default);

        Assert.True(state.IsDraining);
        Assert.False(stopping.IsCompleted);
        Assert.Equal(HealthStatus.Unhealthy, (await CheckAsync(new DrainingHealthCheck(state))).Status);

        clock.Advance(TimeSpan.FromSeconds(5));
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Without_a_drain_period_stopping_does_not_wait()
    {
        var state = new ShutdownState();
        var service = new DrainOnShutdownService(
            state, Options.Create(new ShutdownOptions()), new FakeTimeProvider(), NullLogger<DrainOnShutdownService>.Instance);

        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(new DrainingHealthCheck(state))).Status);
        await service.StoppingAsync(default);

        Assert.True(state.IsDraining);
    }

    [Fact]
    public async Task The_database_check_reports_a_reachable_database_as_healthy()
    {
        await using var database = await SqliteChatDatabase.CreateAsync();
        await using var db = database.NewContext();

        var result = await CheckAsync(new DatabaseHealthCheck<ChatDbContext>(db));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static Task<HealthCheckResult> CheckAsync(IHealthCheck check) =>
        check.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", check, HealthStatus.Unhealthy, tags: null),
        });
}
