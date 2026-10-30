namespace ST16.VM;

/// <summary>Carries one completed instruction to <see cref="ST16Machine.InstructionCompleted"/>.</summary>
public sealed class InstructionCompletedEventArgs : EventArgs
{
    public InstructionCompletedEventArgs(long index, ushort pc, DecodedInstruction instruction)
    {
        Index = index;
        Pc = pc;
        Instruction = instruction;
    }

    /// <summary>0-based ordinal of the completed instruction.</summary>
    public long Index { get; }

    /// <summary>PC of the completed instruction (before the PC advanced).</summary>
    public ushort Pc { get; }

    public DecodedInstruction Instruction { get; }
}
