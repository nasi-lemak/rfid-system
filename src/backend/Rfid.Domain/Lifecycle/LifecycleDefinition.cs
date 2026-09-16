namespace Rfid.Domain.Lifecycle;

/// <summary>
/// Configurable per-item-type state machine. Transitions are keyed by the
/// OperationType that drives them ("on"); "*" matches any current state.
/// </summary>
public class LifecycleDefinition
{
    public string? Initial { get; set; }
    public List<string> States { get; set; } = new();
    public List<LifecycleTransition> Transitions { get; set; } = new();

    public LifecycleTransition? FindTransition(string? currentState, OperationType on, string? targetState = null) => FindTransition(currentState, on.ToString(), targetState);

    /// <summary>Transitions are keyed by the operation code that drives them (a built-in type name or a custom definition code).</summary>
    public LifecycleTransition? FindTransition(string? currentState, string on, string? targetState = null)
    {
        var candidates = Transitions.Where(t =>
            string.Equals(t.On, on, StringComparison.OrdinalIgnoreCase) &&
            (t.From == "*" || string.Equals(t.From, currentState, StringComparison.OrdinalIgnoreCase)) &&
            (targetState == null || string.Equals(t.To, targetState, StringComparison.OrdinalIgnoreCase)));
        // Prefer exact-from matches over wildcards.
        return candidates.OrderBy(t => t.From == "*" ? 1 : 0).FirstOrDefault();
    }

    public bool HasTransitionsFor(string on) => Transitions.Any(t => string.Equals(t.On, on, StringComparison.OrdinalIgnoreCase));

    public bool IsValidState(string? state) =>
        state == null || States.Count == 0 || States.Contains(state, StringComparer.OrdinalIgnoreCase);
}

public class LifecycleTransition
{
    public string From { get; set; } = "*";
    public string To { get; set; } = "";
    public string On { get; set; } = "";
    public bool IncrementCycle { get; set; }
}
