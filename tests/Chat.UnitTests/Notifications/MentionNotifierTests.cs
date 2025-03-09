using System.Net;
using System.Text.Json;
using Chat.Application.Realtime;
using Chat.Contracts;
using Chat.Infrastructure.Notifications;
using Chat.Infrastructure.Presence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Chat.UnitTests.Notifications;

public sealed class MentionNotifierTests : IDisposable
{
    private static readonly Guid Room = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(At);
    private readonly InMemoryPresenceTracker _presence;
    private readonly RecordingSender _sender = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly MentionNotifier _notifier;

    public MentionNotifierTests()
    {
        var options = Options.Create(new NotificationOptions { ExcerptLength = 10 });
        _presence = new InMemoryPresenceTracker(_clock, Options.Create(new PresenceOptions()));
        _notifier = new MentionNotifier(_presence, _sender, new InMemoryNotificationLedger(_cache, options), options);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task Only_mentioned_users_without_a_connection_are_notified()
    {
        await _presence.ConnectAsync("bob", "bob-laptop");

        var sent = await _notifier.HandleAsync(Mention("bob", "carol"), default);

        Assert.Equal(1, sent);
        var notification = Assert.Single(_sender.Sent);
        Assert.Equal(("carol", "alice", Room, 7L), (notification.UserName, notification.Sender, notification.RoomId, notification.MessageId));
    }

    [Fact]
    public async Task Long_messages_are_cut_to_an_excerpt()
    {
        await _notifier.HandleAsync(Mention("carol"), default);

        Assert.Equal("@carol ha…", Assert.Single(_sender.Sent).Excerpt);
    }

    [Fact]
    public async Task The_same_event_read_twice_notifies_once()
    {
        var envelope = Mention("carol");

        await _notifier.HandleAsync(envelope, default);
        await _notifier.HandleAsync(envelope, default);

        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task A_failed_send_is_retried_without_repeating_the_ones_that_went_out()
    {
        var envelope = Mention("bob", "carol");
        _sender.FailFor = "carol";

        await Assert.ThrowsAsync<HttpRequestException>(() => _notifier.HandleAsync(envelope, default));
        _sender.FailFor = null;
        await _notifier.HandleAsync(envelope, default);

        Assert.Equal(["bob", "carol"], _sender.Sent.Select(n => n.UserName));
    }

    [Fact]
    public async Task Other_events_and_messages_without_mentions_are_ignored()
    {
        Assert.Equal(0, await _notifier.HandleAsync(EventEnvelope.Wrap(new MemberJoined(Room, "carol", At), Guid.NewGuid()), default));
        Assert.Equal(0, await _notifier.HandleAsync(
            EventEnvelope.Wrap(new MessageSent(Room, 8, "alice", "hi", [], At), Guid.NewGuid()), default));
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task The_webhook_receives_the_notification_as_json_and_failures_surface()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var webhook = new WebhookNotificationSender(http, Options.Create(new NotificationOptions { WebhookUrl = "https://hooks.test/chat" }));
        var notification = new MentionNotification("carol", Room, 7, "alice", "hi", At);

        await webhook.SendAsync(notification, default);

        Assert.Equal(new Uri("https://hooks.test/chat"), handler.LastRequest!.RequestUri);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("carol", body.RootElement.GetProperty("userName").GetString());

        handler.Status = HttpStatusCode.BadGateway;
        await Assert.ThrowsAsync<HttpRequestException>(() => webhook.SendAsync(notification, default));
    }

    private static EventEnvelope Mention(params string[] users) =>
        EventEnvelope.Wrap(
            new MessageSent(Room, 7, "alice", string.Join(' ', users.Select(u => "@" + u)) + " have a look", users, At),
            Guid.NewGuid());

    private sealed class RecordingSender : INotificationSender
    {
        public List<MentionNotification> Sent { get; } = [];

        public string? FailFor { get; set; }

        public Task SendAsync(MentionNotification notification, CancellationToken cancellationToken)
        {
            if (notification.UserName == FailFor)
            {
                throw new HttpRequestException("webhook down");
            }

            Sent.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = status;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status);
        }
    }
}
