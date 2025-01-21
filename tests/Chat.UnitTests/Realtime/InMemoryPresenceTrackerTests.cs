using Chat.Application.Realtime;
using Chat.Infrastructure.Presence;

namespace Chat.UnitTests.Realtime;

public sealed class InMemoryPresenceTrackerTests : PresenceTrackerContract
{
    public InMemoryPresenceTrackerTests() =>
        Tracker = new InMemoryPresenceTracker(Clock, Microsoft.Extensions.Options.Options.Create(Options));

    protected override IPresenceTracker Tracker { get; }
}
