using Chat.Application.ReadModel;

namespace Chat.Application.Persistence;

/// <summary>
/// The read model, built from published events by the projection. Query handlers project
/// straight to DTOs through these untracked queryables; nothing read here can be saved.
/// It trails the write model by however long the projection takes to apply an event.
/// </summary>
public interface IChatReadDbContext
{
    IQueryable<RoomView> Rooms { get; }

    IQueryable<MemberView> Members { get; }

    IQueryable<MessageView> Messages { get; }

    IQueryable<MessageRevisionView> MessageRevisions { get; }

    IQueryable<UserView> Users { get; }

    IQueryable<DeliveryView> Deliveries { get; }

    IQueryable<SyncEntry> SyncEntries { get; }

    IQueryable<ReadModelState> State { get; }
}
