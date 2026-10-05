using System.Collections.Frozen;

namespace BookStore.Domain.Common;

/// <summary>
/// An explicit table of the transitions a state machine allows. Every lifecycle in
/// the platform is expressed with one of these, so no entity can be moved between
/// states by ad-hoc assignment.
/// </summary>
/// <typeparam name="TState">The enum describing the states.</typeparam>
public sealed class TransitionTable<TState>
    where TState : struct, Enum
{
    private readonly FrozenDictionary<TState, FrozenSet<TState>> _transitions;

    /// <param name="entityName">Used in error messages, for example "Book".</param>
    /// <param name="transitions">Allowed destinations for each source state.</param>
    public TransitionTable(string entityName, IReadOnlyDictionary<TState, TState[]> transitions)
    {
        EntityName = entityName;
        _transitions = transitions.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value.ToFrozenSet());
    }

    public string EntityName { get; }

    /// <summary>True when the move is part of the table.</summary>
    public bool CanTransition(TState from, TState to) =>
        _transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Throws <see cref="InvalidStateTransitionException"/> when the move is not allowed.</summary>
    public void EnsureCanTransition(TState from, TState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidStateTransitionException(EntityName, from.ToString(), to.ToString());
        }
    }

    /// <summary>Every state reachable in one step from <paramref name="from"/>.</summary>
    public IReadOnlyCollection<TState> AllowedFrom(TState from) =>
        _transitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>True when no transition leaves this state.</summary>
    public bool IsTerminal(TState state) => AllowedFrom(state).Count == 0;

    /// <summary>
    /// States that no transition leads to. Useful for tests: every state except the
    /// documented entry points should be reachable.
    /// </summary>
    public IReadOnlyCollection<TState> UnreachableStates()
    {
        var reachable = _transitions.Values.SelectMany(states => states).ToHashSet();
        return [.. Enum.GetValues<TState>().Where(state => !reachable.Contains(state))];
    }
}
