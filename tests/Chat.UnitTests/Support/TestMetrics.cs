using System.Diagnostics.Metrics;
using Chat.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Chat.UnitTests.Support;

/// <summary>A real <see cref="ChatMetrics"/> on its own meter factory, so tests do not share instruments.</summary>
public static class TestMetrics
{
    public static ChatMetrics Create() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());
}
