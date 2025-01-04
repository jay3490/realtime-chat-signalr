using Chat.Domain.Messages;
using Chat.Domain.Rooms;
using Chat.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Persistence;

/// <summary>
/// The write model. Command handlers load aggregates through it, change them and call
/// <see cref="SaveChangesAsync"/>, which also dispatches the domain events they raised.
/// </summary>
public interface IChatDbContext
{
    DbSet<Room> Rooms { get; }

    DbSet<Message> Messages { get; }

    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>True when the exception was raised by a unique index rejecting a row.</summary>
    bool IsUniqueViolation(DbUpdateException exception);

    /// <summary>Forgets everything the context is tracking, e.g. after a failed insert.</summary>
    void DiscardChanges();
}
