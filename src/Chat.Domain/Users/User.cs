using Chat.Domain.Common;

namespace Chat.Domain.Users;

/// <summary>
/// A person who has signed in at least once. Only the name is kept; authentication is
/// handled outside the domain.
/// </summary>
public sealed class User : AggregateRoot
{
    private User()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public static User Register(string name, DateTimeOffset now)
    {
        var user = new User
        {
            Name = UserName.Normalize(name),
            FirstSeenAt = now,
            LastSeenAt = now,
        };

        user.Raise(new UserRegistered(user.Name, now));
        return user;
    }

    public void Touch(DateTimeOffset now) => LastSeenAt = now;
}

public sealed record UserRegistered(string UserName, DateTimeOffset RegisteredAt) : IDomainEvent;
