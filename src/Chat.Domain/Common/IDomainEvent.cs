namespace Chat.Domain.Common;

/// <summary>
/// Marker for something that happened inside an aggregate and that other parts of the
/// system may react to once the change has been persisted.
/// </summary>
public interface IDomainEvent;
