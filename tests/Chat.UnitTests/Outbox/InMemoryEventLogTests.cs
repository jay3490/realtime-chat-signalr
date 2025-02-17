using Chat.Infrastructure.EventLog;
using Chat.Infrastructure.Outbox;

namespace Chat.UnitTests.Outbox;

public class InMemoryEventLogTests
{
    [Fact]
    public void Events_are_read_back_by_offset_per_stream()
    {
        var log = new InMemoryEventLog();
        log.Append("rooms", "a", "first");
        log.Append("rooms", "a", "second");
        log.Append("users", "bob", "third");

        var next = log.Next(new Dictionary<string, long> { ["rooms"] = 1, ["users"] = 1 }, TimeSpan.Zero);

        Assert.Equal(new LoggedEvent("rooms", 1, "a", "second"), next);
        Assert.Equal(2, log.Length("rooms"));
    }

    [Fact]
    public void Next_returns_null_when_nothing_arrives_in_time()
    {
        var log = new InMemoryEventLog();

        Assert.Null(log.Next(new Dictionary<string, long> { ["rooms"] = 0 }, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task A_waiting_reader_wakes_up_when_an_event_is_appended()
    {
        var log = new InMemoryEventLog();
        var reader = Task.Run(() => log.Next(new Dictionary<string, long> { ["rooms"] = 0 }, TimeSpan.FromSeconds(10)));

        await Task.Delay(50);
        log.Append("rooms", "a", "late");

        Assert.Equal("late", (await reader)!.Payload);
    }

    [Fact]
    public async Task The_publisher_appends_the_whole_batch()
    {
        var log = new InMemoryEventLog();
        var publisher = new InMemoryEventPublisher(log);

        var result = await publisher.PublishAsync(
            [new OutgoingEvent(Guid.NewGuid(), "T", "rooms", "a", "1"), new OutgoingEvent(Guid.NewGuid(), "T", "rooms", "a", "2")],
            default);

        Assert.Equal(new PublishResult(2), result);
        Assert.Equal(2, log.Length("rooms"));
    }

    [Fact]
    public async Task Many_notifications_collapse_into_one_wake_up()
    {
        using var signal = new OutboxSignal();
        signal.Notify();
        signal.Notify();

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), default));
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(20), default));
    }
}
