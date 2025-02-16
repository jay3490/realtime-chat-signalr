using Chat.Domain.Common;

namespace Chat.Domain.Messages;

/// <param name="Mentions">Members of the room mentioned with <c>@name</c>, not including the sender.</param>
public sealed record MessagePosted(Message Message, IReadOnlyList<string> Mentions) : IDomainEvent;
