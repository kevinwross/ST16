namespace ST16.VM;

/// <summary>
/// Machine state (plan: Running / Paused / Faulted / Halted).
/// <see cref="MachineState.Paused"/> is the debug-ready transport state; the
/// spec's initial "RUNNING" (spec §2.5) means "alive" — a freshly loaded
/// machine is Paused holding exactly the spec §2.5 initial register values.
/// </summary>
public enum MachineState
{
    Paused,
    Running,
    Faulted,
    Halted,
}

/// <summary>Why the most recent <c>Run</c> stopped (or why a step faulted).</summary>
public enum RunStopReason
{
    None,
    Breakpoint,
    Fault,
    Halted,
    Limit,
}
