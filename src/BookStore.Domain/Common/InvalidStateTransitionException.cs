namespace BookStore.Domain.Common;

/// <summary>
/// Raised when a state machine is asked to perform a transition that is not part
/// of its allowed transition table. Mapped to HTTP 409 by the API layer.
/// </summary>
public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string entity, string from, string to)
        : base($"{entity} cannot move from '{from}' to '{to}'.")
    {
        Entity = entity;
        From = from;
        To = to;
    }

    public string Entity { get; }

    public string From { get; }

    public string To { get; }

    public override string Code => "invalid_state_transition";
}
