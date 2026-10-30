namespace ST16.VM;

/// <summary>
/// Complete machine state for snapshot/restore (plan P2: powers the GUI's
/// undo and "what-if" stepping). Breakpoints and watchpoints are debugger
/// settings, not machine state, and are intentionally not snapshotted.
/// </summary>
public sealed class MachineSnapshot
{
    public ushort Pc { get; init; }
    public MachineState State { get; init; }
    public long InstructionCount { get; init; }
    public FaultCode LastFault { get; init; }
    public ushort FaultPc { get; init; }
    public RunStopReason LastRunStop { get; init; }

    /// <summary>Occupied operand-stack slots, top (s1) first.</summary>
    public ushort[] OperandStackTopDown { get; init; } = [];
    public int OperandCount { get; init; }

    /// <summary>Call-stack frames, most recent first.</summary>
    public ushort[] CallStackTopDown { get; init; } = [];
    public int CallCount { get; init; }

    /// <summary>Full 61,440-word RAM image.</summary>
    public ushort[] Ram { get; init; } = [];

    /// <summary>The machine configuration the snapshot was taken on.</summary>
    public St16Config Config { get; init; } = new();
}
