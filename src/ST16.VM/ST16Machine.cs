namespace ST16.VM;

/// <summary>
/// The ST16 virtual machine (spec v1.0, docs/ST16-ISA.md): a deterministic,
/// debug-hook-first fetch/decode/execute engine. A VM is a pure function of
/// (program, input sequence) — no threads, no clocks, no randomness.
/// </summary>
public sealed class ST16Machine
{
    private readonly Memory _memory = new();
    private readonly FixedWordStack _operandStack;
    private readonly FixedWordStack _callStack;
    private readonly HashSet<ushort> _breakpoints = new();
    private ushort? _resumeSkipBreakpoint;
    private readonly HashSet<ushort> _watchpoints = new();

    public ST16Machine(IInputSource input, IOutputSink output, St16Config? config = null)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Config = config ?? new St16Config();
        _operandStack = new FixedWordStack(Config.OperandDepth);
        _callStack = new FixedWordStack(Config.CallDepth);
    }

    public St16Config Config { get; }
    public IInputSource Input { get; }
    public IOutputSink Output { get; }

    #region State

    /// <summary>Current machine state (Running / Paused / Faulted / Halted).</summary>
    public MachineState State { get; private set; } = MachineState.Paused;

    /// <summary>Program counter (16-bit, word-granular).</summary>
    public ushort Pc { get; private set; }

    /// <summary>
    /// Instruction counter: increments after each completed instruction,
    /// including HALT (spec §7). Port 0xF002 exposes its low 16 bits.
    /// </summary>
    public long InstructionCount { get; private set; }

    /// <summary>Last fault code (<see cref="FaultCode.None"/> unless State is Faulted).</summary>
    public FaultCode LastFault { get; private set; } = FaultCode.None;

    /// <summary>PC of the faulting instruction (valid when State is Faulted).</summary>
    public ushort FaultPc { get; private set; }

    /// <summary>Why the most recent <see cref="Run"/> stopped (or why a step faulted).</summary>
    public RunStopReason LastRunStop { get; private set; } = RunStopReason.None;

    /// <summary>Instruction-limit watchdog: Run/StepOver/StepOut stop here. 0 = unlimited.</summary>
    public long InstructionLimit { get; set; }

    #endregion

    #region Debug events (the phase-5 GUI consumes this API)

    /// <summary>Fires after each completed instruction, including HALT (spec §7).</summary>
    public event Action<InstructionCompletedEventArgs>? InstructionCompleted;

    /// <summary>Fires when the machine faults: (code, PC of the faulting instruction).</summary>
    public event Action<FaultCode, ushort>? FaultOccurred;

    /// <summary>Fires on HALT with the termination code (always 0 in v1).</summary>
    public event Action<ushort>? Halted;

    /// <summary>Fires when <see cref="Run"/> stops at a breakpoint, with the breakpoint address.</summary>
    public event Action<ushort>? BreakpointHit;

    /// <summary>Fires when a committed write touches a watched address (write-only).</summary>
    public event Action<ushort>? WatchpointHit;

    #endregion

    #region Load / reset

    /// <summary>
    /// Loads a little-endian 16-bit word image at 0x0000 and resets the machine
    /// to its initial state (spec §2.5).
    /// </summary>
    public void Load(ReadOnlySpan<byte> littleEndianBinary)
    {
        if (littleEndianBinary.Length % 2 != 0)
            throw new ArgumentException(
                "Instruction files are little-endian 16-bit words: the byte count must be even.",
                nameof(littleEndianBinary));
        int words = littleEndianBinary.Length / 2;
        if (words > Memory.RamSize)
            throw new ArgumentException(
                $"Image of {words} words exceeds the 61,440-word RAM region (spec §3).",
                nameof(littleEndianBinary));
        Reset();
        for (int i = 0; i < words; i++)
            _memory.Write((ushort)i, (ushort)(littleEndianBinary[2 * i] | (littleEndianBinary[2 * i + 1] << 8)));
    }

    /// <summary>Loads a word image at 0x0000 and resets the machine (spec §2.5).</summary>
    public void Load(ReadOnlySpan<ushort> words)
    {
        if (words.Length > Memory.RamSize)
            throw new ArgumentException(
                $"Image of {words.Length} words exceeds the 61,440-word RAM region (spec §3).",
                nameof(words));
        Reset();
        for (int i = 0; i < words.Length; i++)
            _memory.Write((ushort)i, words[i]);
    }

    /// <summary>
    /// Full machine reset (spec §2.5): PC = 0, both stacks empty, RAM zero-filled,
    /// instruction counter 0. Breakpoints and watchpoints are debugger settings
    /// and survive a reset.
    /// </summary>
    public void Reset()
    {
        _memory.Clear();
        _operandStack.Clear();
        _callStack.Clear();
        Pc = 0;
        InstructionCount = 0;
        LastFault = FaultCode.None;
        FaultPc = 0;
        LastRunStop = RunStopReason.None;
        _resumeSkipBreakpoint = null;
        State = MachineState.Paused;
    }

    #endregion

    #region Run control

    /// <summary>
    /// Runs until a breakpoint, fault, HALT, or the instruction-limit watchdog
    /// (a stop, not a fault). In v1 faults and HALT are always terminal
    /// (break-on-fault / break-on-HALT are reserved for v1.1 trap vectors).
    /// Continuing from a breakpoint stop skips that breakpoint on the first
    /// fetch (the machine is already there), so a resume runs on instead of
    /// re-stopping immediately.
    /// </summary>
    public void Run()
    {
        if (State is MachineState.Faulted or MachineState.Halted)
            return;
        State = MachineState.Running;
        while (State == MachineState.Running)
        {
            if (AtInstructionLimit())
            {
                State = MachineState.Paused;
                LastRunStop = RunStopReason.Limit;
                return;
            }
            bool skipCurrentFetch = _resumeSkipBreakpoint == Pc;
            _resumeSkipBreakpoint = null;
            if (_breakpoints.Contains(Pc) && !skipCurrentFetch)
            {
                State = MachineState.Paused;
                LastRunStop = RunStopReason.Breakpoint;
                _resumeSkipBreakpoint = Pc;
                BreakpointHit?.Invoke(Pc);
                return;
            }
            ExecuteOne();
        }
    }

    /// <summary>
    /// Executes exactly one instruction. Returns false when the machine
    /// faulted; true when the instruction completed (including HALT).
    /// </summary>
    public bool StepInto()
    {
        if (State is MachineState.Faulted or MachineState.Halted)
            return false;
        State = MachineState.Running;
        bool completed = ExecuteOne();
        if (State == MachineState.Running)
            State = MachineState.Paused;
        return completed;
    }

    /// <summary>
    /// Steps one instruction; a CALL runs to the end of its frame (the matching
    /// RET) without stopping on inner breakpoints (plan P2: "skips into a CALL
    /// frame and stops at the return").
    /// </summary>
    public bool StepOver()
    {
        if (State is MachineState.Faulted or MachineState.Halted)
            return false;
        if (!CurrentInstructionIsCall())
            return StepInto();
        int depthBeforeCall = _callStack.Count;
        State = MachineState.Running;
        if (!ExecuteOne())
            return false;
        while (_callStack.Count > depthBeforeCall && State == MachineState.Running)
        {
            if (AtInstructionLimit())
            {
                State = MachineState.Paused;
                LastRunStop = RunStopReason.Limit;
                return true;
            }
            if (!ExecuteOne())
                return false;
        }
        if (State == MachineState.Running)
            State = MachineState.Paused;
        return true;
    }

    /// <summary>
    /// Runs until the current call frame returns; at the top level (empty call
    /// stack) behaves as <see cref="StepInto"/>.
    /// </summary>
    public bool StepOut()
    {
        if (State is MachineState.Faulted or MachineState.Halted)
            return false;
        int depthBefore = _callStack.Count;
        if (depthBefore == 0)
            return StepInto();
        State = MachineState.Running;
        while (_callStack.Count >= depthBefore && State == MachineState.Running)
        {
            if (AtInstructionLimit())
            {
                State = MachineState.Paused;
                LastRunStop = RunStopReason.Limit;
                return true;
            }
            if (!ExecuteOne())
                return false;
        }
        if (State == MachineState.Running)
            State = MachineState.Paused;
        return true;
    }

    /// <summary>True when the instruction-limit watchdog would stop execution now.</summary>
    public bool AtInstructionLimit() => InstructionLimit != 0 && InstructionCount >= InstructionLimit;

    #endregion

    #region Breakpoints / watchpoints

    public void SetBreakpoint(ushort address) => _breakpoints.Add(address);

    public void ClearBreakpoint(ushort address) => _breakpoints.Remove(address);

    public bool HasBreakpoint(ushort address) => _breakpoints.Contains(address);

    public IReadOnlyList<ushort> Breakpoints => _breakpoints.OrderBy(a => a).ToList();

    /// <summary>
    /// Registers a write-only watchpoint: <see cref="WatchpointHit"/> fires when a
    /// committed STORE (RAM or mapped port) or a debug <see cref="WriteMemory"/>
    /// touches the address.
    /// </summary>
    public void SetWatchpoint(ushort address) => _watchpoints.Add(address);

    public void ClearWatchpoint(ushort address) => _watchpoints.Remove(address);

    public bool HasWatchpoint(ushort address) => _watchpoints.Contains(address);

    public IReadOnlyList<ushort> Watchpoints => _watchpoints.OrderBy(a => a).ToList();

    #endregion

    #region State inspection

    /// <summary>Full operand stack: index 0 is the top (s1); unoccupied slots read 0.</summary>
    public IReadOnlyList<ushort> OperandStack
    {
        get
        {
            var list = new ushort[_operandStack.Capacity];
            for (int i = 0; i < _operandStack.Count; i++)
                list[i] = _operandStack.At(i);
            return list;
        }
    }

    /// <summary>Number of occupied operand-stack slots.</summary>
    public int OperandCount => _operandStack.Count;

    /// <summary>Call-stack frames, most recent (top) first; empty at the top level.</summary>
    public IReadOnlyList<ushort> CallStack => _callStack.TopDownCopy();

    /// <summary>Call-stack depth (number of outstanding CALL frames).</summary>
    public int CallDepth => _callStack.Count;

    /// <summary>Reads a RAM word (0x0000–0xEFFF). For ports use <see cref="ReadPort"/>.</summary>
    public ushort ReadMemory(ushort address)
    {
        if (!Memory.IsRamAddress(address))
            throw new ArgumentException($"0x{address:X4} is a port address; use ReadPort.", nameof(address));
        return _memory.Read(address);
    }

    /// <summary>Writes a RAM word. Fires <see cref="WatchpointHit"/> when watched (debug write).</summary>
    public void WriteMemory(ushort address, ushort value)
    {
        if (!Memory.IsRamAddress(address))
            throw new ArgumentException($"0x{address:X4} is a port address; use WritePort.", nameof(address));
        _memory.Write(address, value);
        if (_watchpoints.Contains(address))
            WatchpointHit?.Invoke(address);
    }

    /// <summary>Reads <paramref name="count"/> RAM words starting at <paramref name="start"/>.</summary>
    public ushort[] MemoryWindow(ushort start, int count) => _memory.Window(start, count);

    /// <summary>
    /// Reads a mapped I/O port (spec §7): 0xF000 → 0; 0xF001 → next input byte or
    /// 0xFFFF; 0xF002 → instruction counter (low 16 bits). Unmapped ports throw
    /// here; the machine itself faults with ADDRF on LOAD/STORE to them.
    /// </summary>
    public ushort ReadPort(ushort port)
    {
        switch (port)
        {
            case Memory.ConsoleOutPort:
                return 0;
            case Memory.ConsoleInPort:
                return Input.TryReadByte(out byte inputByte) ? (ushort)inputByte : Memory.ConsoleInNoInput;
            case Memory.InstructionCounterPort:
                return (ushort)(InstructionCount & 0xFFFF);
            default:
                throw new ArgumentException($"Port 0x{port:X4} is unmapped (spec §7).", nameof(port));
        }
    }

    /// <summary>
    /// Writes a mapped I/O port (spec §7): 0xF000 outputs the low 8 bits;
    /// 0xF001 and 0xF002 are ignored (no-op). Unmapped ports throw here.
    /// </summary>
    public void WritePort(ushort port, ushort value)
    {
        switch (port)
        {
            case Memory.ConsoleOutPort:
                Output.WriteByte((byte)value);
                break;
            case Memory.ConsoleInPort:
            case Memory.InstructionCounterPort:
                break;
            default:
                throw new ArgumentException($"Port 0x{port:X4} is unmapped (spec §7).", nameof(port));
        }
    }

    #endregion

    #region Snapshot / restore

    public MachineSnapshot TakeSnapshot() => new()
    {
        Pc = Pc,
        State = State,
        InstructionCount = InstructionCount,
        LastFault = LastFault,
        FaultPc = FaultPc,
        LastRunStop = LastRunStop,
        OperandStackTopDown = _operandStack.TopDownCopy(),
        OperandCount = _operandStack.Count,
        CallStackTopDown = _callStack.TopDownCopy(),
        CallCount = _callStack.Count,
        Ram = _memory.FullCopy(),
        Config = Config,
    };

    /// <summary>Restores a snapshot taken on a machine with identical stack depths.</summary>
    public void Restore(MachineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Config.OperandDepth != Config.OperandDepth || snapshot.Config.CallDepth != Config.CallDepth)
            throw new ArgumentException(
                "Snapshot was taken on a machine with a different stack-depth configuration.",
                nameof(snapshot));
        _memory.Restore(snapshot.Ram);
        _operandStack.Restore(snapshot.OperandStackTopDown, snapshot.OperandCount);
        _callStack.Restore(snapshot.CallStackTopDown, snapshot.CallCount);
        Pc = snapshot.Pc;
        State = snapshot.State;
        InstructionCount = snapshot.InstructionCount;
        LastFault = snapshot.LastFault;
        FaultPc = snapshot.FaultPc;
        LastRunStop = snapshot.LastRunStop;
    }

    #endregion

    #region Execute engine

    /// <summary>
    /// Fetch/decode/execute one instruction (spec §4–§7). Returns false when the
    /// machine faults. Atomicity (spec §2.3): every fault condition is checked
    /// before any machine state is mutated, so a faulting instruction has no
    /// partial effects.
    /// </summary>
    internal bool ExecuteOne()
    {
        if (State is MachineState.Faulted or MachineState.Halted)
            return false;

        ushort pc = Pc;

        // --- Fetch (spec §3: an instruction fetch from the port region is ADDRF).
        if (!Memory.IsRamAddress(pc))
            return Fault(FaultCode.AddrF, pc);

        ushort raw = _memory.Read(pc);

        // --- Decode (spec §4: ILLOP when bits 15–14 are set or the opcode is reserved).
        if ((raw & 0xC000) != 0)
            return Fault(FaultCode.Illop, pc);
        byte opcodeByte = (byte)(raw >> 8);
        if (opcodeByte > OpcodeTable.LastDefinedOpcode)
            return Fault(FaultCode.Illop, pc);
        Opcode op = (Opcode)opcodeByte;
        ushort operand = (ushort)(raw & 0xFF);
        OpcodeInfo info = OpcodeTable.Get(op);

        // Two-word instructions carry their absolute 16-bit value in the next word (spec §4).
        ushort? second = null;
        if (info.TwoWord)
        {
            ushort secondAddress = (ushort)(pc + 1);
            if (!Memory.IsRamAddress(secondAddress))
                return Fault(FaultCode.AddrF, pc);
            second = _memory.Read(secondAddress);
        }

        // Fault priority (spec §6): operand underflow before instruction-specific faults.
        if (_operandStack.Count < info.MinStackDepth)
            return Fault(FaultCode.StackUf, pc);

        // --- Execute. Every remaining fault — DIV0, ADDRF, and STACKOF for the
        // net-push instructions (checked in the cases below, after any per-
        // instruction ADDRF condition) — is checked before any mutation, so the
        // atomicity guarantee holds.
        var wa = new Word(_operandStack.Count >= 2 ? _operandStack.At(1) : (ushort)0);
        var wb = new Word(_operandStack.Count >= 1 ? _operandStack.Top : (ushort)0);
        ushort newPc = (ushort)(pc + (info.TwoWord ? 2 : 1));

        switch (op)
        {
            case Opcode.Dup:
                if (_operandStack.IsFull)
                    return Fault(FaultCode.StackOf, pc);
                _operandStack.Push(_operandStack.Top);
                break;
            case Opcode.Swap:
                _operandStack.SwapTop2();
                break;
            case Opcode.Over:
                if (_operandStack.IsFull)
                    return Fault(FaultCode.StackOf, pc);
                _operandStack.Push(_operandStack.At(1));
                break;
            case Opcode.Rot:
            {
                ushort top = _operandStack.At(0);
                ushort secondSlot = _operandStack.At(1);
                ushort thirdSlot = _operandStack.At(2);
                _operandStack.SetSlot(0, thirdSlot);
                _operandStack.SetSlot(1, top);
                _operandStack.SetSlot(2, secondSlot);
                break;
            }
            case Opcode.Drop:
                _operandStack.Pop();
                break;
            case Opcode.Add:
                ReplaceTop2(wa + wb);
                break;
            case Opcode.Sub:
                ReplaceTop2(wa - wb);
                break;
            case Opcode.Neg:
                ReplaceTop1(Word.Negate(wb));
                break;
            case Opcode.Mul:
                ReplaceTop2(wa * wb);
                break;
            case Opcode.Div:
                if (wb.Raw == 0)
                    return Fault(FaultCode.Div0, pc);
                ReplaceTop2(Word.TruncDivide(wa, wb));
                break;
            case Opcode.Mod:
                if (wb.Raw == 0)
                    return Fault(FaultCode.Div0, pc);
                ReplaceTop2(Word.Mod(wa, wb));
                break;
            case Opcode.And:
                ReplaceTop2(wa & wb);
                break;
            case Opcode.Or:
                ReplaceTop2(wa | wb);
                break;
            case Opcode.Xor:
                ReplaceTop2(wa ^ wb);
                break;
            case Opcode.Not:
                ReplaceTop1(~wb);
                break;
            case Opcode.Sll:
                ReplaceTop2(Word.ShiftLeft(wb, wa.Raw));
                break;
            case Opcode.Slr:
                ReplaceTop2(Word.ShiftRightArithmetic(wb, wa.Raw));
                break;
            case Opcode.Eq:
                PushCompare(wa.Raw == wb.Raw);
                break;
            case Opcode.Ne:
                PushCompare(wa.Raw != wb.Raw);
                break;
            case Opcode.Lt:
                PushCompare(Word.Slt(wa, wb));
                break;
            case Opcode.Gt:
                PushCompare(Word.Sgt(wa, wb));
                break;
            case Opcode.Le:
                PushCompare(Word.Sle(wa, wb));
                break;
            case Opcode.Ge:
                PushCompare(Word.Sge(wa, wb));
                break;
            case Opcode.Ult:
                PushCompare(Word.Ult(wa, wb));
                break;
            case Opcode.Ugt:
                PushCompare(Word.Ugt(wa, wb));
                break;
            case Opcode.Ule:
                PushCompare(Word.Ule(wa, wb));
                break;
            case Opcode.Uge:
                PushCompare(Word.Uge(wa, wb));
                break;
            case Opcode.Push:
                // Signed 8-bit immediate, sign-extended to 16 bits (spec §4 + erratum).
                if (_operandStack.IsFull)
                    return Fault(FaultCode.StackOf, pc);
                _operandStack.Push(Word.SignExtend8((byte)operand).Raw);
                break;
            case Opcode.Push16:
                if (_operandStack.IsFull)
                    return Fault(FaultCode.StackOf, pc);
                _operandStack.Push(second!.Value);
                break;
            case Opcode.Load:
            {
                ushort address = wb.Raw;
                ushort value;
                if (Memory.IsRamAddress(address))
                    value = _memory.Read(address);
                else
                {
                    // Mapped port reads (spec §7): 0xF000 → 0, 0xF001 → input or 0xFFFF,
                    // 0xF002 → instruction counter; 0xF003+ → ADDRF.
                    if (address > Memory.InstructionCounterPort)
                        return Fault(FaultCode.AddrF, pc);
                    value = ReadPort(address);
                }
                if (_operandStack.IsFull)
                    return Fault(FaultCode.StackOf, pc);
                ReplaceTop1(new Word(value));
                break;
            }
            case Opcode.Store:
            {
                ushort address = wb.Raw; // top of stack is the address (spec §5.4)
                ushort value = wa.Raw;    // second is the value
                if (Memory.IsRamAddress(address))
                    _memory.Write(address, value);
                else
                {
                    // Mapped port writes (spec §7): 0xF000 → console out, 0xF001/0xF002 → no-op;
                    // 0xF003+ → ADDRF.
                    if (address > Memory.InstructionCounterPort)
                        return Fault(FaultCode.AddrF, pc);
                    WritePort(address, value);
                }
                _operandStack.Pop();
                _operandStack.Pop();
                if (_watchpoints.Contains(address))
                    WatchpointHit?.Invoke(address);
                break;
            }
            case Opcode.Jmp:
                newPc = second!.Value;
                break;
            case Opcode.Call:
                // CALL pushes PC + 2 (the word after the two-word CALL), then PC ← t (spec §5.5).
                if (_callStack.IsFull)
                    return Fault(FaultCode.CallOf, pc);
                _callStack.Push((ushort)(pc + 2));
                newPc = second!.Value;
                break;
            case Opcode.Ret:
                if (_callStack.IsEmpty)
                    return Fault(FaultCode.CallUf, pc);
                newPc = _callStack.Pop();
                break;
            case Opcode.Jz:
                _operandStack.Pop();
                if (wb.Raw == 0)
                    newPc = (ushort)(pc + 1 + (int)(sbyte)operand);
                break;
            case Opcode.Jnz:
                _operandStack.Pop();
                if (wb.Raw != 0)
                    newPc = (ushort)(pc + 1 + (int)(sbyte)operand);
                break;
            case Opcode.Halt:
                newPc = pc; // HALT leaves the PC on the HALT word (post-mortem view).
                break;
            default:
                // Unreachable: decode already rejected undefined opcodes.
                return Fault(FaultCode.Illop, pc);
        }

        // --- Commit: PC, instruction counter (spec §7: it increments after each
        // completed instruction, including HALT), and the completion event.
        Pc = newPc;
        InstructionCount++;
        if (op == Opcode.Halt)
        {
            State = MachineState.Halted;
            LastRunStop = RunStopReason.Halted;
            Halted?.Invoke(0);
        }
        DecodedInstruction decoded = new(pc, op, raw, operand, second, info.Mnemonic);
        InstructionCompleted?.Invoke(new InstructionCompletedEventArgs(InstructionCount - 1, pc, decoded));
        return true;
    }

    /// <summary>Pops the top two stack values and pushes one result.</summary>
    private void ReplaceTop2(Word result)
    {
        _operandStack.Pop();
        _operandStack.Pop();
        _operandStack.Push(result.Raw);
    }

    /// <summary>Replaces the top stack value.</summary>
    private void ReplaceTop1(Word result)
    {
        _operandStack.Pop();
        _operandStack.Push(result.Raw);
    }

    /// <summary>Pops the top two values and pushes 1 or 0 (comparisons, spec §5.3).</summary>
    private void PushCompare(bool result)
    {
        _operandStack.Pop();
        _operandStack.Pop();
        _operandStack.Push((ushort)(result ? 1 : 0));
    }

    /// <summary>True when the instruction at the current PC is a CALL (StepOver dispatch).</summary>
    private bool CurrentInstructionIsCall()
    {
        if (!Memory.IsRamAddress(Pc))
            return false;
        ushort raw = _memory.Read(Pc);
        if ((raw & 0xC000) != 0)
            return false;
        return (byte)(raw >> 8) == (byte)Opcode.Call;
    }

    /// <summary>Enters the FAULTED state and reports the fault (spec §2.3, §6: no partial effects).</summary>
    private bool Fault(FaultCode code, ushort pc)
    {
        State = MachineState.Faulted;
        LastFault = code;
        FaultPc = pc;
        LastRunStop = RunStopReason.Fault;
        FaultOccurred?.Invoke(code, pc);
        return false;
    }

    #endregion
}
