using Chat.Application.ReadModel;
using Chat.Contracts;
using Chat.Infrastructure.EventLog;
using RoomKind = Chat.Domain.Rooms.RoomKind;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Chat.Infrastructure.ReadModel;

public enum ProjectionOutcome
{
    /// <summary>The event changed the read model.</summary>
    Applied,

    /// <summary>The event id was applied before (a re-sent copy at a later offset); only the checkpoint moved.</summary>
    Duplicate,

    /// <summary>The offset is at or behind the checkpoint, so it was applied before; nothing changed.</summary>
    AlreadyProjected,

    /// <summary>The payload could not be read; the checkpoint moved past it so the projection is not stuck.</summary>
    Unreadable,
}

/// <summary>
/// Applies events to the read model, one transaction per event. Each transaction also
/// records the event id and the partition's offset, which makes replays harmless: an
/// offset at or behind the checkpoint is skipped, and an id already seen is ignored.
/// Every handler is additionally written so that applying an event twice changes nothing.
/// </summary>
public sealed partial class ReadModelProjector(ReadModelDbContext db, TimeProvider clock, ILogger<ReadModelProjector> logger)
{
    /// <summary>The next offset to read for a partition, or null to start from the beginning.</summary>
    public async Task<long?> ResumeOffsetAsync(string stream, int partition, CancellationToken cancellationToken)
    {
        var checkpoint = await db.Checkpoints.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Stream == stream && c.Partition == partition, cancellationToken);
        return checkpoint is null ? null : checkpoint.Offset + 1;
    }

    /// <summary>
    /// Synchronous form of <see cref="ResumeOffsetAsync"/> for the Kafka client's partition
    /// assignment callback, which runs on the consuming thread and cannot be awaited.
    /// </summary>
    public long? ResumeOffset(string stream, int partition)
    {
        var checkpoint = db.Checkpoints.AsNoTracking()
            .SingleOrDefault(c => c.Stream == stream && c.Partition == partition);
        return checkpoint is null ? null : checkpoint.Offset + 1;
    }

    public async Task<ProjectionOutcome> ProjectAsync(ConsumedEvent consumed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumed);
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var checkpoint = await db.Checkpoints
            .SingleOrDefaultAsync(c => c.Stream == consumed.Stream && c.Partition == consumed.Partition, cancellationToken);
        if (checkpoint is not null && consumed.Offset <= checkpoint.Offset)
        {
            return ProjectionOutcome.AlreadyProjected;
        }

        ProjectionOutcome outcome;
        if (consumed.Envelope is not { } envelope)
        {
            LogUnreadable(logger, consumed.Stream, consumed.Partition, consumed.Offset, consumed.Error);
            outcome = ProjectionOutcome.Unreadable;
        }
        else if (await db.ProcessedEvents.AnyAsync(e => e.EventId == envelope.EventId, cancellationToken))
        {
            outcome = ProjectionOutcome.Duplicate;
        }
        else
        {
            await ApplyAsync(envelope.Event, cancellationToken);
            db.ProcessedEvents.Add(new ProcessedEvent
            {
                EventId = envelope.EventId,
                EventType = envelope.EventType,
                ProcessedAt = clock.GetUtcNow(),
            });
            outcome = ProjectionOutcome.Applied;
        }

        if (checkpoint is null)
        {
            db.Checkpoints.Add(new ProjectionCheckpoint { Stream = consumed.Stream, Partition = consumed.Partition, Offset = consumed.Offset });
        }
        else
        {
            checkpoint.Offset = consumed.Offset;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return outcome;
    }

    private Task ApplyAsync(IChatEvent chatEvent, CancellationToken cancellationToken) => chatEvent switch
    {
        RoomCreated e => ApplyAsync(e, cancellationToken),
        MemberJoined e => ApplyAsync(e, cancellationToken),
        MessageSent e => ApplyAsync(e, cancellationToken),
        MessageEdited e => ApplyAsync(e, cancellationToken),
        MessageDeleted e => ApplyAsync(e, cancellationToken),
        ReadReceiptRecorded e => ApplyAsync(e, cancellationToken),
        UserRegistered e => ApplyAsync(e, cancellationToken),
        _ => Task.CompletedTask,
    };

    private async Task ApplyAsync(RoomCreated e, CancellationToken cancellationToken)
    {
        if (await db.Rooms.AnyAsync(r => r.Id == e.RoomId, cancellationToken))
        {
            return;
        }

        db.Rooms.Add(new RoomView
        {
            Id = e.RoomId,
            Kind = Enum.Parse<RoomKind>(e.Kind),
            Name = e.Name,
            CreatedAt = e.CreatedAt,
            MemberCount = e.Members.Count,
        });
        db.Members.AddRange(e.Members.Select(member => new MemberView { RoomId = e.RoomId, UserName = member, JoinedAt = e.CreatedAt }));
    }

    private async Task ApplyAsync(MemberJoined e, CancellationToken cancellationToken)
    {
        if (await db.Members.AnyAsync(m => m.RoomId == e.RoomId && m.UserName == e.UserName, cancellationToken))
        {
            return;
        }

        // A new member has read nothing, so everything already said by others is unread.
        var unread = await db.Messages.CountAsync(
            m => m.RoomId == e.RoomId && m.Sender != e.UserName && m.DeletedAt == null, cancellationToken);
        db.Members.Add(new MemberView { RoomId = e.RoomId, UserName = e.UserName, JoinedAt = e.JoinedAt, UnreadCount = unread });

        if (await db.Rooms.FindAsync([e.RoomId], cancellationToken) is { } room)
        {
            room.MemberCount++;
        }
    }

    private async Task ApplyAsync(MessageSent e, CancellationToken cancellationToken)
    {
        if (await db.Messages.AnyAsync(m => m.Id == e.MessageId, cancellationToken))
        {
            return;
        }

        db.Messages.Add(new MessageView
        {
            Id = e.MessageId,
            RoomId = e.RoomId,
            Sender = e.Sender,
            Body = e.Body,
            SentAt = e.SentAt,
            Revision = 1,
        });
        db.MessageRevisions.Add(new MessageRevisionView { MessageId = e.MessageId, Revision = 1, Body = e.Body, RecordedAt = e.SentAt });

        if (await db.Rooms.FindAsync([e.RoomId], cancellationToken) is { } room && e.MessageId > room.LastMessageId)
        {
            room.LastMessageId = e.MessageId;
            room.LastActivityAt = e.SentAt;
        }

        await db.Members
            .Where(m => m.RoomId == e.RoomId && m.UserName != e.Sender && m.LastReadMessageId < e.MessageId)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.UnreadCount, m => m.UnreadCount + 1), cancellationToken);
    }

    private async Task ApplyAsync(MessageEdited e, CancellationToken cancellationToken)
    {
        var message = await db.Messages.FindAsync([e.MessageId], cancellationToken);
        if (message is null || message.DeletedAt is not null || e.Revision <= message.Revision)
        {
            return;
        }

        message.Body = e.Body;
        message.Revision = e.Revision;
        message.EditedAt = e.EditedAt;
        db.MessageRevisions.Add(new MessageRevisionView { MessageId = e.MessageId, Revision = e.Revision, Body = e.Body, RecordedAt = e.EditedAt });
    }

    private async Task ApplyAsync(MessageDeleted e, CancellationToken cancellationToken)
    {
        var message = await db.Messages.FindAsync([e.MessageId], cancellationToken);
        if (message is null || message.DeletedAt is not null)
        {
            return;
        }

        // The tombstone keeps the message's place; its text and earlier versions go.
        message.Body = string.Empty;
        message.DeletedAt = e.DeletedAt;
        await db.MessageRevisions.Where(r => r.MessageId == e.MessageId).ExecuteDeleteAsync(cancellationToken);

        await db.Members
            .Where(m => m.RoomId == e.RoomId && m.UserName != e.Sender && m.LastReadMessageId < e.MessageId && m.UnreadCount > 0)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.UnreadCount, m => m.UnreadCount - 1), cancellationToken);
    }

    private async Task ApplyAsync(ReadReceiptRecorded e, CancellationToken cancellationToken)
    {
        var member = await db.Members.FindAsync([e.RoomId, e.UserName], cancellationToken);
        if (member is null || e.LastReadMessageId <= member.LastReadMessageId)
        {
            return;
        }

        member.LastReadMessageId = e.LastReadMessageId;
        member.LastReadAt = e.ReadAt;
        member.UnreadCount = await db.Messages.CountAsync(
            m => m.RoomId == e.RoomId && m.Id > e.LastReadMessageId && m.Sender != e.UserName && m.DeletedAt == null,
            cancellationToken);
    }

    private async Task ApplyAsync(UserRegistered e, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Name == e.UserName, cancellationToken))
        {
            db.Users.Add(new UserView { Name = e.UserName, RegisteredAt = e.RegisteredAt });
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Skipping unreadable event at {Stream}/{Partition}@{Offset}: {Error}")]
    private static partial void LogUnreadable(ILogger logger, string stream, int partition, long offset, string? error);
}
