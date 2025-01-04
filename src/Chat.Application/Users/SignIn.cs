using Chat.Application.Abstractions;
using Chat.Application.Persistence;
using Chat.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Chat.Application.Users;

/// <summary>
/// Records that a user has signed in, registering them on first use. Returns the
/// normalized user name that should be used as their identity.
/// </summary>
public sealed record SignInCommand(string UserName) : ICommand<string>;

internal sealed class SignInHandler(IChatDbContext db, TimeProvider clock) : ICommandHandler<SignInCommand, string>
{
    public async Task<string> HandleAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        var name = UserName.Normalize(command.UserName);
        var now = clock.GetUtcNow();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Name == name, cancellationToken);
        if (user is null)
        {
            db.Users.Add(User.Register(name, now));
        }
        else
        {
            user.Touch(now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // A concurrent first sign-in registered the same name; that is fine.
            db.DiscardChanges();
        }

        return name;
    }
}
