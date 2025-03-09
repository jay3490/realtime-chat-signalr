using Chat.Application.Realtime;
using Chat.Contracts;
using Microsoft.Extensions.Options;

namespace Chat.Infrastructure.Notifications;

/// <summary>
/// Turns mentions into notifications for people who are not connected. Someone with an open
/// connection already sees the message, highlighted, so they are skipped.
/// </summary>
public sealed class MentionNotifier(
    IPresenceTracker presence,
    INotificationSender sender,
    INotificationLedger ledger,
    IOptions<NotificationOptions> options)
{
    /// <summary>Returns how many notifications were sent for the event.</summary>
    public async Task<int> HandleAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Event is not MessageSent message || message.Mentions.Count == 0)
        {
            return 0;
        }

        var sent = 0;
        foreach (var userName in message.Mentions.Where(u => !string.Equals(u, message.Sender, StringComparison.Ordinal)))
        {
            if ((await presence.GetConnectionsAsync(userName, cancellationToken)).Count > 0)
            {
                continue;
            }

            var key = $"{envelope.EventId:N}:{userName}";
            if (!await ledger.TryClaimAsync(key, cancellationToken))
            {
                continue;
            }

            try
            {
                await sender.SendAsync(
                    new MentionNotification(userName, message.RoomId, message.MessageId, message.Sender, Excerpt(message.Body), message.SentAt),
                    cancellationToken);
                sent++;
            }
            catch
            {
                await ledger.ReleaseAsync(key, CancellationToken.None);
                throw;
            }
        }

        return sent;
    }

    private string Excerpt(string body)
    {
        var length = options.Value.ExcerptLength;
        return body.Length <= length ? body : string.Concat(body.AsSpan(0, length - 1), "…");
    }
}
