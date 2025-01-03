namespace Chat.Domain.Common;

/// <summary>
/// Thrown when an operation would break one of the domain's invariants.
/// </summary>
public sealed class DomainRuleException(string message) : Exception(message);
