namespace Chat.Application.Common;

/// <summary>Base type for failures a client caused and can be told about.</summary>
public abstract class ChatException(string message) : Exception(message);

public sealed class NotFoundException(string message) : ChatException(message);

public sealed class ForbiddenException(string message) : ChatException(message);

public sealed class ConflictException(string message) : ChatException(message);

public sealed class RequestValidationException(string message) : ChatException(message);
