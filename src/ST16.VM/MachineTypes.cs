namespace ST16.VM;

/// <summary>High-level machine state (spec §2.5, §6).</summary>
public enum MachineState
{
    Running,
    Paused,
    Faulted,
    Halted,
}

/// <summary>Why the machine is not Running.</summary>
public enum StopReason
{
    None,
    Breakpoint,
    Fault,
    LimitReached,
    Halted,
}

/// <summary>Single-step granularity (informational; see ST16Machine.Step methods).</summary>
public enum StepMode
{
    StepInto,
    StepOver,
    StepOut,
}

/// <summary>Console input source for port 0xF001. Returns 0xFFFF when no input is available (EOF).</summary>
public interface IInputSource
{
    /// <summary>Read one character; 0xFFFF when empty.</summary>
    ushort ReadChar();
}

/// <summary>Console output sink for port 0xF000.</summary>
public interface IOutputSink
{
    /// <summary>Emit one byte (low 8 bits of the stored word).</summary>
    void WriteChar(byte b);
}

/// <summary>Queue-backed input source (deterministic tests, CLI `in` command).</summary>
public sealed class InMemoryInput : IInputSource
{
    private readonly Queue<byte> _queue;

    public InMemoryInput(IEnumerable<byte> data) => _queue = new Queue<byte>(data);

    public InMemoryInput(string text) : this(System.Text.Encoding.ASCII.GetBytes(text))
    {
    }

    public ushort ReadChar() => _queue.Count > 0 ? _queue.Dequeue() : (ushort)0xFFFF;

    /// <summary>Queue additional input at runtime (CLI `in &lt;text&gt;`).</summary>
    public void Enqueue(byte b) => _queue.Enqueue(b);

    public int Pending => _queue.Count;
}

/// <summary>Collecting output sink.</summary>
public sealed class InMemoryOutput : IOutputSink
{
    private readonly List<byte> _bytes = new();

    public void WriteChar(byte b) => _bytes.Add(b);

    public byte[] ToBytes() => _bytes.ToArray();

    public string AsText() => System.Text.Encoding.ASCII.GetString(_bytes.ToArray());
}

/// <summary>Input source that is always empty (0xFFFF).</summary>
public sealed class EmptyInputSource : IInputSource
{
    public ushort ReadChar() => 0xFFFF;
}

/// <summary>Console input; blocks until a character or EOF (0xFFFF at EOF).</summary>
public sealed class ConsoleInputSource : IInputSource
{
    public ushort ReadChar()
    {
        int c = Console.In.Read();
        return c < 0 ? (ushort)0xFFFF : (ushort)(c & 0xFF);
    }
}

/// <summary>Console output sink.</summary>
public sealed class ConsoleOutputSink : IOutputSink
{
    public void WriteChar(byte b) => Console.Out.Write((char)b);
}

/// <summary>
/// Machine configuration (spec §2.1: stack depth is machine configuration).
/// Depths are clamped to 4…256; the reference machine uses 32/32.
/// </summary>
public sealed class ST16Config
{
    public static ST16Config Default { get; } = new();

    public const int MinDepth = 4;
    public const int MaxDepth = 256;

    private int _operandStackDepth = 32;
    private int _callStackDepth = 32;

    /// <summary>Operand stack depth in slots (4…256; default 32).</summary>
    public int OperandStackDepth
    {
        get => _operandStackDepth;
        init => _operandStackDepth = CheckDepth(value, nameof(OperandStackDepth));
    }

    /// <summary>Call stack depth in slots (4…256; default 32).</summary>
    public int CallStackDepth
    {
        get => _callStackDepth;
        init => _callStackDepth = CheckDepth(value, nameof(CallStackDepth));
    }

    /// <summary>Maximum instructions per run; 0 = unlimited (default).</summary>
    public uint InstructionLimit { get; init; }

    public ST16Config()
    {
    }

    private static int CheckDepth(int value, string name)
    {
        if (value < MinDepth || value > MaxDepth)
            throw new ArgumentOutOfRangeException(name, value,
                $"Stack depth must be between {MinDepth} and {MaxDepth} slots.");
        return value;
    }
}

/// <summary>Raised after every completed instruction (including HALT; not fired on a fault).</summary>
public sealed class InstructionCompletedEventArgs
{
    public InstructionCompletedEventArgs(uint index, ushort pc, ushort opcode, ushort operand, ushort word)
    {
        Index = index; PC = pc; Opcode = opcode; Operand = operand; Word = word;
    }

    /// <summary>0-based sequence number of this instruction (count before execution).</summary>
    public uint Index { get; }

    /// <summary>PC of the instruction's header word.</summary>
    public ushort PC { get; }

    /// <summary>Decoded opcode (bits 15–8, values 0x00–0x3F).</summary>
    public ushort Opcode { get; }

    /// <summary>Header operand field (low byte, 0…255).</summary>
    public ushort Operand { get; }

    /// <summary>The header word as fetched.</summary>
    public ushort Word { get; }
}

/// <summary>Raised when the machine faults (spec §6).</summary>
public sealed class FaultEventArgs
{
    public FaultEventArgs(int code, ushort pc)
    {
        Code = code; PC = pc;
    }

    /// <summary>Fault code 1…7 (spec §6).</summary>
    public int Code { get; }

    /// <summary>PC of the faulting instruction.</summary>
    public ushort PC { get; }
}

/// <summary>Raised when HALT is executed (normal completion, not a fault).</summary>
public sealed class HaltedEventArgs
{
    public HaltedEventArgs(ushort pc) => PC = pc;

    /// <summary>PC of the HALT instruction.</summary>
    public ushort PC { get; }
}

/// <summary>Raised when execution pauses at a breakpoint (before the instruction executes).</summary>
public sealed class BreakpointHitEventArgs
{
    public BreakpointHitEventArgs(ushort pc) => PC = pc;

    /// <summary>PC of the (not yet executed) instruction.</summary>
    public ushort PC { get; }
}

/// <summary>Full machine state (spec §2.5), restorable.</summary>
public sealed class MachineSnapshot
{
    public ushort PC { get; init; }
    public MachineState State { get; init; }
    public StopReason StopReason { get; init; }
    public uint InstructionCount { get; init; }
    public int FaultCode { get; init; }
    public ushort FaultPC { get; init; }

    /// <summary>Operand stack, [0] = top.</summary>
    public ushort[] OperandStack { get; init; } = Array.Empty<ushort>();

    /// <summary>Call stack, [0] = most recent return address.</summary>
    public ushort[] CallStack { get; init; } = Array.Empty<ushort>();

    /// <summary>All 61,440 RAM words (0x0000–0xEFFF).</summary>
    public ushort[] Memory { get; init; } = Array.Empty<ushort>();

    public HashSet<ushort> Breakpoints { get; init; } = new();
}
