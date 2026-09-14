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

    public LifecycleTransition? FindTransition(string? currentState, OperationType on, string? targetState = null)
    {
        var candidates = Transitions.Where(t =>
            string.Equals(t.On, on.ToString(), StringComparison.OrdinalIgnoreCase) &&
            (t.From == "*" || string.Equals(t.From, currentState, StringComparison.OrdinalIgnoreCase)) &&
            (targetState == null || string.Equals(t.To, targetState, StringComparison.OrdinalIgnoreCase)));
        // Prefer exact-from matches over wildcards.
        return candidates.OrderBy(t => t.From == "*" ? 1 : 0).FirstOrDefault();
    }

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
