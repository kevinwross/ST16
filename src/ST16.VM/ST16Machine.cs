using ST16.VM;

namespace ST16.VM;

/// <summary>
/// The ST16 virtual CPU: a 16-bit, pure stack-based machine (ISA v1.0, docs/ST16-ISA.md).
/// Encoding: word = (opcode &lt;&lt; 8) | operand — opcode in the high byte (0x00–0x3F,
/// bits 15–14 must be 0), signed 8-bit immediate in the low byte. Two-word forms:
/// PUSH16, JMP, CALL. For `a b` (a second-from-top, b top) the instruction computes
/// `a OP b`; SLL/SLR take the value on top and the count second.
/// </summary>
public sealed class ST16Machine
{
    // --- memory map (spec §3, §7) ---
    public const ushort PortOut = 0xF000;       // console out (low 8 bits)
    public const ushort PortIn = 0xF001;        // console in (0xFFFF when empty)
    public const ushort PortCounter = 0xF002;   // instruction counter
    public const int RamSize = 0xF000;          // 61,440 words: 0x0000–0xEFFF

    // --- fault codes (spec §6) ---
    public const int FaultDiv0 = 1;
    public const int FaultStackUf = 2;
    public const int FaultStackOf = 3;
    public const int FaultIllop = 4;
    public const int FaultAddrF = 5;
    public const int FaultCallOf = 6;
    public const int FaultCallUf = 7;

    // --- opcodes (spec §5) ---
    private const ushort OpDup = 0x00, OpSwap = 0x01, OpOver = 0x02, OpRot = 0x03, OpDrop = 0x04;
    private const ushort OpAdd = 0x05, OpSub = 0x06, OpNeg = 0x07, OpMul = 0x08, OpDiv = 0x09;
    private const ushort OpMod = 0x0A, OpAnd = 0x0B, OpOr = 0x0C, OpXor = 0x0D, OpNot = 0x0E;
    private const ushort OpSll = 0x0F, OpSlr = 0x10;
    private const ushort OpEq = 0x11, OpNe = 0x12, OpLt = 0x13, OpGt = 0x14, OpLe = 0x15, OpGe = 0x16;
    private const ushort OpUlt = 0x17, OpUgt = 0x18, OpUle = 0x19, OpUge = 0x1A;
    private const ushort OpPush = 0x1B, OpPush16 = 0x1C, OpLoad = 0x1D, OpStore = 0x1E;
    private const ushort OpJmp = 0x1F, OpCall = 0x20, OpRet = 0x21, OpJz = 0x22, OpJnz = 0x23, OpHalt = 0x24;

    private readonly ST16Config _config;
    private readonly IInputSource _input;
    private readonly IOutputSink _output;
    private readonly ushort[] _ram = new ushort[RamSize];
    private readonly ushort[] _operand;
    private int _operandDepth;
    private readonly ushort[] _call;
    private int _callDepth;
    private readonly HashSet<ushort> _breakpoints = new();

    private ushort _pc;
    private MachineState _state = MachineState.Running;
    private StopReason _stopReason = StopReason.None;
    private uint _instructionCount;
    private int _faultCode;
    private ushort _faultPc;

    private uint _instructionLimit;

    public ST16Machine(ST16Config? config = null, IInputSource? input = null, IOutputSink? output = null)
    {
        _config = config ?? ST16Config.Default;
        _input = input ?? new EmptyInputSource();
        _output = output ?? new InMemoryOutput();
        _operand = new ushort[_config.OperandStackDepth];
        _call = new ushort[_config.CallStackDepth];
        _instructionLimit = _config.InstructionLimit;
    }

    /// <summary>Instruction limit (0 = unlimited); settable at runtime (debugger `limit` command).</summary>
    public uint InstructionLimit
    {
        get => _instructionLimit;
        set => _instructionLimit = value;
    }

    public ST16Config Config => _config;

    // --- observable state ---
    public MachineState State => _state;
    public StopReason StopReason => _stopReason;
    public ushort PC => _pc;
    public uint InstructionCount => _instructionCount;
    public int FaultCode => _faultCode;
    public ushort FaultPC => _faultPc;

    /// <summary>Single-step granularity (informational; Step() is StepInto, StepOver()/StepOut() are explicit).</summary>
    public StepMode StepMode { get; set; } = StepMode.StepInto;

    /// <summary>v1: faults are always terminal (spec §6); reserved for v1.1 trap vectors.</summary>
    public bool BreakOnFaultEnabled { get; set; } = true;

    /// <summary>v1: HALT is always terminal; reserved for v1.1.</summary>
    public bool BreakOnHalt { get; set; } = true;

    /// <summary>Operand stack, [0] = top.</summary>
    public IReadOnlyList<ushort> OperandStack => ToTopFirst(_operand, _operandDepth);

    /// <summary>Call stack, [0] = most recent return address.</summary>
    public IReadOnlyList<ushort> CallStack => ToTopFirst(_call, _callDepth);

    // --- program management ---

    /// <summary>
    /// Load a program at <paramref name="origin"/> (default 0x0000) and reset the machine (spec §2.5).
    /// Returns false if the program does not fit in RAM (0x0000–0xEFFF).
    /// </summary>
    public bool LoadProgram(ReadOnlySpan<ushort> words, ushort origin = 0x0000)
    {
        if ((int)origin + words.Length > RamSize) return false;
        Reset();
        for (int i = 0; i < words.Length; i++) _ram[origin + i] = words[i];
        return true;
    }

    /// <summary>Reset to the spec §2.5 initial state (clears RAM, stacks, counters, breakpoints).</summary>
    public void Reset()
    {
        _pc = 0;
        _state = MachineState.Running;
        _stopReason = StopReason.None;
        _instructionCount = 0;
        _faultCode = 0;
        _faultPc = 0;
        _operandDepth = 0;
        _callDepth = 0;
        Array.Clear(_ram);
        _breakpoints.Clear();
    }

    /// <summary>Resume from a Paused state (breakpoint / instruction limit). Faulted/Halted are terminal.</summary>
    public void Resume()
    {
        if (_state == MachineState.Paused)
        {
            _state = MachineState.Running;
            _stopReason = StopReason.None;
        }
    }

    // --- RAM access (debugger use; ports excluded) ---

    public ushort ReadMemory(ushort addr)
    {
        if (addr >= PortOut)
            throw new ArgumentOutOfRangeException(nameof(addr),
                $"ReadMemory is RAM-only (0x0000–0xEFFF); got 0x{addr:X4}.");
        return _ram[addr];
    }

    public void WriteMemory(ushort addr, ushort value)
    {
        if (addr >= PortOut)
            throw new ArgumentOutOfRangeException(nameof(addr),
                $"WriteMemory is RAM-only (0x0000–0xEFFF); got 0x{addr:X4}.");
        _ram[addr] = value;
    }

    // --- breakpoints ---

    public void SetBreakpoint(ushort addr) => _breakpoints.Add(addr);
    public void ClearBreakpoint(ushort addr) => _breakpoints.Remove(addr);
    public void ClearBreakpoints() => _breakpoints.Clear();
    public bool HasBreakpoint(ushort addr) => _breakpoints.Contains(addr);

    // --- execution ---

    /// <summary>
    /// Execute exactly one instruction (StepInto). Returns false if the machine is not
    /// Running (nothing executed). A breakpoint at the current PC pauses BEFORE execution.
    /// </summary>
    public bool Step()
    {
        if (_state != MachineState.Running) return false;

        if (_breakpoints.Contains(_pc))
        {
            _state = MachineState.Paused;
            _stopReason = StopReason.Breakpoint;
            BreakpointHit?.Invoke(new BreakpointHitEventArgs(_pc));
            return true;
        }

        ushort pc = _pc;
        ushort header = _ram[pc];
        if ((header & 0xC000) != 0)          // bits 15–14 must be 0
        {
            RaiseFault(FaultIllop, pc);
            return true;
        }
        ushort op = (ushort)(header >> 8);
        ushort operand = (ushort)(header & 0xFF);
        uint index = _instructionCount;

        bool twoWord = false;
        bool branch = false;

        switch (op)
        {
            // ---- 5.1 stack manipulation ----
            case OpDup:
                if (_operandDepth < 1) { RaiseFault(FaultStackUf, pc); return true; }
                if (_operandDepth == _operand.Length) { RaiseFault(FaultStackOf, pc); return true; }
                Push(_operand[_operandDepth - 1]);        // value read before the push (C#: RHS before side effects)
                break;

            case OpSwap:
                if (_operandDepth < 2) { RaiseFault(FaultStackUf, pc); return true; }
                {
                    ushort t = _operand[_operandDepth - 1];
                    _operand[_operandDepth - 1] = _operand[_operandDepth - 2];
                    _operand[_operandDepth - 2] = t;
                }
                break;

            case OpOver:
                if (_operandDepth < 2) { RaiseFault(FaultStackUf, pc); return true; }
                if (_operandDepth == _operand.Length) { RaiseFault(FaultStackOf, pc); return true; }
                Push(_operand[_operandDepth - 2]);        // value read before the push (C#: RHS before side effects)
                break;

            case OpRot:   // a b c -> b c a  (c on top)
                if (_operandDepth < 3) { RaiseFault(FaultStackUf, pc); return true; }
                {
                    int d = _operandDepth;
                    ushort a = _operand[d - 3], b = _operand[d - 2], c = _operand[d - 1];
                    _operand[d - 3] = b; _operand[d - 2] = c; _operand[d - 1] = a;
                }
                break;

            case OpDrop:
                if (_operandDepth < 1) { RaiseFault(FaultStackUf, pc); return true; }
                _operandDepth--;
                break;

            // ---- 5.2 arithmetic (a OP b: a second, b top; wraps mod 2^16) ----
            case OpAdd:
                if (Underflow(pc, 2)) return true;
                {
                    ushort b = Pop(), a = Pop();
                    Push((ushort)(a + b));
                }
                break;

            case OpSub:   // a - b (top is the subtrahend)
                if (Underflow(pc, 2)) return true;
                {
                    ushort b = Pop(), a = Pop();
                    Push((ushort)(a - b));
                }
                break;

            case OpNeg:
                if (Underflow(pc, 1)) return true;
                {
                    ushort b = Pop();
                    Push((ushort)(0 - b));     // 0x8000 -> 0x8000 (wrap)
                }
                break;

            case OpMul:
                if (Underflow(pc, 2)) return true;
                {
                    ushort b = Pop(), a = Pop();
                    Push((ushort)((uint)a * b));
                }
                break;

            case OpDiv:   // a / b, truncated toward zero; 0x8000 / -1 -> 0x8000 (no fault)
                if (Underflow(pc, 2)) return true;
                if (_operand[_operandDepth - 1] == 0) { RaiseFault(FaultDiv0, pc); return true; }  // check before popping (atomicity)
                {
                    ushort b = Pop(), a = Pop();
                    int ia = (int)(short)a, ib = (int)(short)b;
                    Push(ia == int.MinValue && ib == -1 ? (ushort)0x8000 : (ushort)(ia / ib));
                }
                break;

            case OpMod:   // a mod b, sign of the dividend (C-99 style)
                if (Underflow(pc, 2)) return true;
                if (_operand[_operandDepth - 1] == 0) { RaiseFault(FaultDiv0, pc); return true; }  // check before popping (atomicity)
                {
                    ushort b = Pop(), a = Pop();
                    int ia = (int)(short)a, ib = (int)(short)b;
                    Push((ushort)(ia % ib));
                }
                break;

            case OpAnd:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push((ushort)(a & b)); }
                break;

            case OpOr:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push((ushort)(a | b)); }
                break;

            case OpXor:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push((ushort)(a ^ b)); }
                break;

            case OpNot:
                if (Underflow(pc, 1)) return true;
                { ushort b = Pop(); Push((ushort)~b); }
                break;

            case OpSll:   // n c -> c << (n & 0xF): value on top, count second
                if (Underflow(pc, 2)) return true;
                {
                    ushort c = Pop(), n = Pop();
                    Push((ushort)(c << (n & 0xF)));
                }
                break;

            case OpSlr:   // n c -> c >> (n & 0xF): arithmetic (sign-extending)
                if (Underflow(pc, 2)) return true;
                {
                    ushort c = Pop(), n = Pop();
                    Push((ushort)(((int)(short)c >> (n & 0xF)) & 0xFFFF));
                }
                break;

            // ---- 5.3 comparison: push 1 iff a OP b (a second, b top) ----
            case OpEq:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(a == b ? (ushort)1 : (ushort)0); }
                break;

            case OpNe:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(a != b ? (ushort)1 : (ushort)0); }
                break;

            case OpLt:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(((int)(short)a < (int)(short)b) ? (ushort)1 : (ushort)0); }
                break;

            case OpGt:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(((int)(short)a > (int)(short)b) ? (ushort)1 : (ushort)0); }
                break;

            case OpLe:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(((int)(short)a <= (int)(short)b) ? (ushort)1 : (ushort)0); }
                break;

            case OpGe:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(((int)(short)a >= (int)(short)b) ? (ushort)1 : (ushort)0); }
                break;

            case OpUlt:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(a < b ? (ushort)1 : (ushort)0); }
                break;

            case OpUgt:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(a > b ? (ushort)1 : (ushort)0); }
                break;

            case OpUle:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(a <= b ? (ushort)1 : (ushort)0); }
                break;

            case OpUge:
                if (Underflow(pc, 2)) return true;
                { ushort b = Pop(), a = Pop(); Push(a >= b ? (ushort)1 : (ushort)0); }
                break;

            // ---- 5.4 memory ----
            case OpPush:
                if (_operandDepth == _operand.Length) { RaiseFault(FaultStackOf, pc); return true; }
                Push((ushort)(sbyte)operand);            // sign-extend the 8-bit immediate
                break;

            case OpPush16:
                if (_operandDepth == _operand.Length) { RaiseFault(FaultStackOf, pc); return true; }
                Push(_ram[(ushort)(pc + 1)]);
                twoWord = true;
                break;

            case OpLoad:
                if (Underflow(pc, 1)) return true;
                {
                    ushort addr = _operand[_operandDepth - 1];
                    if (!TryRead(addr, out ushort v)) { RaiseFault(FaultAddrF, pc); return true; }
                    _operand[_operandDepth - 1] = v;      // address replaced by its value
                }
                break;

            case OpStore:   // v A -> (A on top); validate before popping (atomicity)
                if (Underflow(pc, 2)) return true;
                {
                    ushort addr = _operand[_operandDepth - 1];
                    if (!IsStoreTarget(addr)) { RaiseFault(FaultAddrF, pc); return true; }
                    Pop();                                 // addr
                    ushort v = Pop();
                    ApplyStore(addr, v);
                }
                break;

            // ---- 5.5 control flow ----
            case OpJmp:
                _pc = _ram[(ushort)(pc + 1)];
                twoWord = true;
                branch = true;
                break;

            case OpCall:
                if (_callDepth == _call.Length) { RaiseFault(FaultCallOf, pc); return true; }
                _call[_callDepth++] = (ushort)(pc + 2);
                _pc = _ram[(ushort)(pc + 1)];
                twoWord = true;
                branch = true;
                break;

            case OpRet:
                if (_callDepth == 0) { RaiseFault(FaultCallUf, pc); return true; }
                _pc = _call[--_callDepth];
                branch = true;
                break;

            case OpJz:
                if (Underflow(pc, 1)) return true;
                {
                    ushort x = Pop();
                    int r = (sbyte)operand;               // signed 8-bit relative offset
                    int next = pc + 1;
                    _pc = (ushort)(x == 0 ? next + r : next);
                }
                branch = true;
                break;

            case OpJnz:
                if (Underflow(pc, 1)) return true;
                {
                    ushort x = Pop();
                    int r = (sbyte)operand;
                    int next = pc + 1;
                    _pc = (ushort)(x != 0 ? next + r : next);
                }
                branch = true;
                break;

            case OpHalt:
                break;    // state transition below

            default:      // 0x25–0x3F reserved
                RaiseFault(FaultIllop, pc);
                return true;
        }

        // HALT leaves the PC on the HALT word itself (debugger: stopped at this instruction).
        if (op != OpHalt && !branch) _pc = (ushort)(pc + (twoWord ? 2 : 1));

        _instructionCount++;
        InstructionCompleted?.Invoke(new InstructionCompletedEventArgs(index, pc, op, operand, header));

        if (op == OpHalt)
        {
            _state = MachineState.Halted;
            _stopReason = StopReason.Halted;
            Halted?.Invoke(new HaltedEventArgs(pc));
            return true;
        }

        if (_instructionLimit != 0 && _instructionCount >= _instructionLimit)
        {
            _state = MachineState.Paused;
            _stopReason = StopReason.LimitReached;
        }

        return true;
    }

    /// <summary>Step until the machine is not Running (honors breakpoints, faults, limit, HALT).</summary>
    public void Run()
    {
        while (_state == MachineState.Running) Step();
    }

    /// <summary>
    /// One instruction, or a full call: if the instruction at PC is CALL, keep stepping
    /// until that frame's RET (call-stack depth drops below the post-call depth).
    /// </summary>
    public bool StepOver()
    {
        if (_state != MachineState.Running) return false;

        int frameDepthAfterCall = -1;
        {
            ushort header = _ram[_pc];
            if ((header & 0xC000) == 0 && (ushort)(header >> 8) == OpCall && _callDepth < _call.Length)
                frameDepthAfterCall = _callDepth + 1;
        }

        Step();
        while (_state == MachineState.Running && (frameDepthAfterCall < 0 || _callDepth >= frameDepthAfterCall))
            Step();
        return true;
    }

    /// <summary>
    /// Step until leaving the current call frame (depth &lt; depth at entry).
    /// With no frames on the call stack, steps exactly one instruction.
    /// </summary>
    public bool StepOut()
    {
        if (_state != MachineState.Running) return false;

        int depthAtStart = _callDepth;
        if (depthAtStart == 0)
        {
            Step();
            return true;
        }
        while (_state == MachineState.Running && _callDepth >= depthAtStart)
            Step();
        return true;
    }

    // --- snapshots ---

    public MachineSnapshot TakeSnapshot()
    {
        return new MachineSnapshot
        {
            PC = _pc,
            State = _state,
            StopReason = _stopReason,
            InstructionCount = _instructionCount,
            FaultCode = _faultCode,
            FaultPC = _faultPc,
            OperandStack = ToTopFirst(_operand, _operandDepth),
            CallStack = ToTopFirst(_call, _callDepth),
            Memory = (ushort[])_ram.Clone(),
            Breakpoints = new HashSet<ushort>(_breakpoints),
        };
    }

    /// <summary>Restore a snapshot (must come from a machine with the same configured depths).</summary>
    public void Restore(MachineSnapshot snapshot)
    {
        _pc = snapshot.PC;
        _state = snapshot.State;
        _stopReason = snapshot.StopReason;
        _instructionCount = snapshot.InstructionCount;
        _faultCode = snapshot.FaultCode;
        _faultPc = snapshot.FaultPC;

        if (snapshot.Memory.Length == _ram.Length)
            Array.Copy(snapshot.Memory, _ram, snapshot.Memory.Length);

        RestoreStack(_operand, ref _operandDepth, snapshot.OperandStack);
        RestoreStack(_call, ref _callDepth, snapshot.CallStack);

        _breakpoints.Clear();
        foreach (ushort bp in snapshot.Breakpoints) _breakpoints.Add(bp);
    }

    // --- I/O ports (spec §7) ---

    private bool IsStoreTarget(ushort addr) =>
        addr < PortOut || addr == PortOut || addr == PortIn || addr == PortCounter;

    private bool TryRead(ushort addr, out ushort value)
    {
        if (addr < PortOut) { value = _ram[addr]; return true; }
        switch (addr)
        {
            case PortOut: value = 0; return true;
            case PortIn: value = _input.ReadChar(); return true;
            case PortCounter: value = (ushort)_instructionCount; return true;
            default: value = 0; return false;   // 0xF003–0xFFFF unmapped
        }
    }

    private void ApplyStore(ushort addr, ushort value)
    {
        if (addr < PortOut) { _ram[addr] = value; return; }
        switch (addr)
        {
            case PortOut: _output.WriteChar((byte)value); break;
            case PortIn:
            case PortCounter:
                break;   // writes ignored (no-op)
        }
    }

    // --- stack helpers ---

    private ushort Pop() => _operand[--_operandDepth];
    private void Push(ushort v) => _operand[_operandDepth++] = v;

    /// <summary>Check underflow; returns true (machine faulted) if it occurred.</summary>
    private bool Underflow(ushort pc, int need)
    {
        if (_operandDepth < need)
        {
            RaiseFault(FaultStackUf, pc);
            return true;
        }
        return false;
    }

    private void RaiseFault(int code, ushort pc)
    {
        _faultCode = code;
        _faultPc = pc;
        _state = MachineState.Faulted;
        _stopReason = StopReason.Fault;
        FaultOccurred?.Invoke(new FaultEventArgs(code, pc));
    }

    private static ushort[] ToTopFirst(ushort[] storage, int depth)
    {
        var arr = new ushort[depth];
        for (int i = 0; i < depth; i++) arr[i] = storage[depth - 1 - i];
        return arr;
    }

    private static void RestoreStack(ushort[] storage, ref int depth, ushort[] topFirst)
    {
        depth = topFirst.Length;
        for (int i = 0; i < depth; i++) storage[i] = topFirst[depth - 1 - i];
    }

    // --- events ---

    public event Action<InstructionCompletedEventArgs>? InstructionCompleted;
    public event Action<FaultEventArgs>? FaultOccurred;
    public event Action<HaltedEventArgs>? Halted;
    public event Action<BreakpointHitEventArgs>? BreakpointHit;
}
