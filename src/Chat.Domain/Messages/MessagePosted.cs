using Chat.Domain.Common;

namespace Chat.Domain.Messages;

public sealed record MessagePosted(Message Message) : IDomainEvent;
