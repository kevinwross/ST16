using System.IO;
using System.Text;
using ST16.Assembler;
using ST16.Disassembler;
using ST16.VM;

namespace ST16.Gui;

/// <summary>
/// The debugger session: owns one <see cref="ST16Machine"/> plus its debug
/// info and exposes the whole run-control / inspect surface the WPF shell
/// renders. Deliberately UI-free (no WPF types) so the integration tests
/// drive the exact same code path the GUI uses.
/// </summary>
public sealed class DebugSession
{
    /// <summary>Undo history cap (machine snapshots are full RAM images).</summary>
    private const int MaxUndo = 256;

    private readonly SessionInputSource _input;
    private readonly SessionOutputSink _output;
    private readonly ST16Machine _machine;
    private readonly Stack<MachineSnapshot> _undo = new();

    private ushort[] _words = [];
    private St16dFile? _symbols;
    private string? _sourceName;
    private Disassembly? _disassembly;
    private Dictionary<int, LineInfo>? _lineByAddress;
    private Dictionary<(int File, int Line), int>? _firstAddressByLine;
    private List<SymbolInfo>? _symbolsSorted;

    public DebugSession()
    {
        _input = new SessionInputSource();
        _output = new SessionOutputSink(this);
        _machine = new ST16Machine(_input, _output);
    }

    #region Load

    /// <summary>
    /// Loads a program by file extension:
    /// <c>.st16</c> (assembled here), <c>.bin</c> (image + optional sibling
    /// <c>.st16d</c>), or <c>.c</c> (its compiled siblings <c>.bin</c> +
    /// <c>.st16d</c> from <c>st16 cc</c>).
    /// </summary>
    public void LoadFile(string path)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".st16":
            {
                var text = File.ReadAllText(path);
                var result = St16Assembler.Assemble(text, name);
                Load(result.Words.ToArray(), result.DebugSymbols, text, name);
                break;
            }
            case ".bin":
            {
                LoadImage(path);
                break;
            }
            case ".c":
            {
                var bin = Path.ChangeExtension(path, ".bin");
                if (!File.Exists(bin))
                    throw new InvalidOperationException(
                        $"{name} has not been compiled yet — run `st16 cc {name}` first (it produces {Path.GetFileName(bin)} + .st16d).");
                LoadImage(bin);
                break;
            }
            default:
                throw new InvalidOperationException(
                    $"unsupported file type '{ext}' — expected .st16, .c, or .bin");
        }
    }

    private void LoadImage(string binPath)
    {
        var bytes = File.ReadAllBytes(binPath);
        if (bytes.Length % 2 != 0)
            throw new InvalidOperationException($"{Path.GetFileName(binPath)} is not a little-endian 16-bit word image (odd byte count).");
        var words = new ushort[bytes.Length / 2];
        for (int i = 0; i < words.Length; i++)
            words[i] = (ushort)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
        St16dFile? symbols = null;
        var st16dPath = Path.ChangeExtension(binPath, ".st16d");
        if (File.Exists(st16dPath))
            symbols = St16dFile.FromJson(File.ReadAllText(st16dPath));
        Load(words, symbols, null, Path.GetFileName(binPath));
    }

    /// <summary>Loads words + debug info and resets the machine to the entry point.</summary>
    public void Load(ushort[] words, St16dFile? symbols, string? sourceText, string name)
    {
        _words = words;
        _symbols = symbols;
        _sourceName = name;
        _undo.Clear();

        _machine.Load(words);

        _disassembly = St16Disassembler.Disassemble(words, symbols);

        _lineByAddress = new Dictionary<int, LineInfo>();
        _firstAddressByLine = new Dictionary<(int, int), int>();
        if (symbols is not null)
        {
            foreach (var l in symbols.Lines)
            {
                _lineByAddress[l.Address] = l;
                var key = (l.File, l.Line);
                if (!_firstAddressByLine.TryGetValue(key, out var existing) || l.Address < existing)
                    _firstAddressByLine[key] = l.Address;
            }
            _symbolsSorted = symbols.Symbols.OrderBy(s => s.Address).ToList();
        }
        else
        {
            _symbolsSorted = null;
        }

        _output.Clear();
        _input.Clear();
        Notify();
    }

    /// <summary>The loaded program's name (file name), for the window title.</summary>
    public string? ProgramName => _sourceName;

    #endregion

    #region Console I/O

    /// <summary>Queues input bytes for port 0xF001 (one byte per character, ASCII).</summary>
    public void EnqueueInput(string text)
    {
        foreach (var c in text)
            _input.Enqueue((byte)c);
        Notify();
    }

    /// <summary>Everything the program has written to port 0xF000 since the last clear.</summary>
    public string ConsoleOutput => _output.Text;

    public void ClearConsole()
    {
        _output.Clear();
        Notify();
    }

    /// <summary>Raised when the program writes to the console (live append).</summary>
    public event Action? OutputAppended;

    private void OnOutputAppended() => OutputAppended?.Invoke();

    #endregion

    #region Run control

    /// <summary>
    /// Runs until a breakpoint, fault, HALT, or the instruction-limit watchdog.
    /// The engine is synchronous and deterministic (a VM is a pure function of
    /// program + input), so this completes in one call; <see cref="Pause"/>
    /// exists for toolbar parity and reports that there is nothing to interrupt.
    /// </summary>
    public void Run() => Continue();

    /// <summary>Alias of <see cref="Run"/> (toolbar "Continue").</summary>
    public void Continue()
    {
        Snapshot();
        _machine.Run();
        Notify();
    }

    /// <summary>
    /// The engine runs to completion synchronously; returns false (nothing was
    /// running to interrupt) and the caller should surface that hint.
    /// </summary>
    public bool Pause() => false;

    public void StepInto()
    {
        if (_machine.State is MachineState.Faulted or MachineState.Halted)
            return;
        Snapshot();
        _machine.StepInto();
        Notify();
    }

    public void StepOver()
    {
        if (_machine.State is MachineState.Faulted or MachineState.Halted)
            return;
        Snapshot();
        _machine.StepOver();
        Notify();
    }

    public void StepOut()
    {
        if (_machine.State is MachineState.Faulted or MachineState.Halted)
            return;
        Snapshot();
        _machine.StepOut();
        Notify();
    }

    /// <summary>Resets the machine and re-loads the program (breakpoints survive).</summary>
    public void Reset()
    {
        Snapshot();
        _machine.Load(_words);
        _output.Clear();
        _input.Clear();
        Notify();
    }

    /// <summary>
    /// Jumps the PC to <paramref name="address"/> (symbol navigation, call-stack
    /// click-to-jump): every other machine state is preserved, implemented as a
    /// snapshot/restore with the PC rewritten.
    /// </summary>
    public void JumpTo(int address)
    {
        Snapshot();
        var s = _machine.TakeSnapshot();
        _machine.Restore(new MachineSnapshot
        {
            Pc = (ushort)address,
            State = MachineState.Paused,
            InstructionCount = s.InstructionCount,
            LastFault = s.LastFault,
            FaultPc = s.FaultPc,
            LastRunStop = RunStopReason.None,
            OperandStackTopDown = s.OperandStackTopDown,
            OperandCount = s.OperandCount,
            CallStackTopDown = s.CallStackTopDown,
            CallCount = s.CallCount,
            Ram = s.Ram,
            Config = s.Config,
        });
        Notify();
    }

    /// <summary>Restores the most recent snapshot (undo the last run-control/memory change).</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
            return;
        var snapshot = _undo.Pop();
        _machine.Restore(snapshot);
        Notify();
    }

    public bool CanUndo => _undo.Count > 0;

    /// <summary>0 = unlimited (the VM's watchdog otherwise stops at the limit).</summary>
    public void SetInstructionLimit(long limit)
    {
        _machine.InstructionLimit = Math.Max(0, limit);
        Notify();
    }

    public long InstructionLimit => _machine.InstructionLimit;

    private void Snapshot()
    {
        _undo.Push(_machine.TakeSnapshot());
        if (_undo.Count > MaxUndo)
        {
            // Stack has no RemoveAt; rebuild dropping the oldest.
            var all = _undo.ToArray();
            _undo.Clear();
            for (int i = all.Length - 1; i >= 1; i--)
                _undo.Push(all[i]);
        }
    }

    #endregion

    #region Breakpoints / watchpoints

    public void SetBreakpoint(ushort address) => _machine.SetBreakpoint(address);
    public void ClearBreakpoint(ushort address) => _machine.ClearBreakpoint(address);
    public bool HasBreakpoint(ushort address) => _machine.HasBreakpoint(address);
    public IReadOnlyList<ushort> Breakpoints => _machine.Breakpoints;

    /// <summary>
    /// Toggles the breakpoint on the first instruction mapped to
    /// (file, source line) by the debug info; no-op when the line is unmapped.
    /// </summary>
    public void ToggleBreakpointAtLine(int fileIndex, int line)
    {
        if (_firstAddressByLine is null)
            return;
        if (!_firstAddressByLine.TryGetValue((fileIndex, line), out int address))
            return;
        ToggleBreakpoint((ushort)address);
    }

    public void ToggleBreakpoint(ushort address)
    {
        if (_machine.HasBreakpoint(address))
            _machine.ClearBreakpoint(address);
        else
            _machine.SetBreakpoint(address);
        Notify();
    }

    public void SetWatchpoint(ushort address) => _machine.SetWatchpoint(address);
    public void ClearWatchpoint(ushort address) => _machine.ClearWatchpoint(address);
    public bool HasWatchpoint(ushort address) => _machine.HasWatchpoint(address);
    public IReadOnlyList<ushort> Watchpoints => _machine.Watchpoints;

    /// <summary>Toggles the write-only watchpoint at an address.</summary>
    public void ToggleWatchpoint(ushort address)
    {
        if (_machine.HasWatchpoint(address))
            _machine.ClearWatchpoint(address);
        else
            _machine.SetWatchpoint(address);
        Notify();
    }

    #endregion

    #region Memory

    public ushort ReadWord(ushort address) => _machine.ReadMemory(address);

    public void WriteWord(ushort address, ushort value)
    {
        Snapshot();
        _machine.WriteMemory(address, value);
        Notify();
    }

    public ushort[] MemoryWindow(ushort start, int count) => _machine.MemoryWindow(start, count);

    #endregion

    #region State readout

    public MachineState State => _machine.State;
    public ushort Pc => _machine.Pc;
    public long InstructionCount => _machine.InstructionCount;
    public FaultCode LastFault => _machine.LastFault;
    public ushort FaultPc => _machine.FaultPc;
    public RunStopReason LastRunStop => _machine.LastRunStop;

    /// <summary>
    /// Fault display text: code, faulting PC, the instruction there, and the
    /// operand-stack top for post-mortem. Null when there is no fault.
    /// </summary>
    public string? FaultDescription
    {
        get
        {
            if (State != MachineState.Faulted)
                return null;
            var instruction = _disassembly?.Lines.FirstOrDefault(l => l.Address == FaultPc) is { } dl
                ? dl.Text
                : $"0x{FaultPc:X4}";
            var stack = OperandStack;
            var top = stack.Count > 0 ? $"stack top: 0x{stack[0]:X4}" : "stack empty";
            return $"{LastFault} at 0x{FaultPc:X4}: {instruction} ({top})";
        }
    }

    /// <summary>Occupied operand-stack slots, top (s1) first (zero-padded to capacity).</summary>
    public IReadOnlyList<ushort> OperandStack => _machine.OperandStack;

    /// <summary>Number of occupied operand-stack slots.</summary>
    public int OperandStackCount => _machine.OperandCount;

    /// <summary>Call-stack frames, most recent first (return addresses).</summary>
    public IReadOnlyList<ushort> CallStack => _machine.CallStack;

    #endregion

    #region Views / resolution

    /// <summary>The disassembly of the loaded program (null before load).</summary>
    public Disassembly? Disassembly => _disassembly;

    /// <summary>Embedded source files from the debug info (null when absent).</summary>
    public IReadOnlyList<SourceFile>? Sources => _symbols?.Sources;

    /// <summary>Maps an instruction address to (file, line) via the debug info.</summary>
    public (int File, int Line)? LineForAddress(int address) =>
        _lineByAddress is not null && _lineByAddress.TryGetValue(address, out var l)
            ? (l.File, l.Line)
            : null;

    /// <summary>
    /// Resolves a call-stack frame (return address) to (function name, source
    /// line of the return target); the function is the one whose body contains
    /// the return address.
    /// </summary>
    public (string Function, int? Line) ResolveFrame(ushort returnAddress)
    {
        var function = _symbols?.Functions.FirstOrDefault(f => returnAddress >= f.Entry && returnAddress < f.End);
        var name = function?.Name ?? SymbolAt(returnAddress) ?? "?";
        var line = LineForAddress(returnAddress) is { } fl ? fl.Line : (int?)null;
        return (name, line);
    }

    /// <summary>The address of a named symbol, or null when unknown.</summary>
    public int? AddressOfSymbol(string name) =>
        _symbols?.Symbols.FirstOrDefault(s => s.Name == name)?.Address;

    /// <summary>
    /// The nearest symbol at or below the address (label / function / data
    /// name); null when the address precedes every symbol.
    /// </summary>
    public string? SymbolAt(int address)
    {
        if (_symbolsSorted is null || _symbolsSorted.Count == 0)
            return null;
        int lo = 0, hi = _symbolsSorted.Count - 1, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (_symbolsSorted[mid].Address <= address)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best < 0 ? null : _symbolsSorted[best].Name;
    }

    /// <summary>
    /// Live annotation for the instruction at the PC: the disassembly text
    /// with any absolute operand resolved to a symbol name, plus the current
    /// stack depth (null before load or past the last instruction).
    /// </summary>
    public string? CurrentInstructionAnnotation
    {
        get
        {
            if (_disassembly is null)
                return null;
            var line = _disassembly.Lines.FirstOrDefault(l => l.Address == Pc);
            if (line is null)
                return null;
            string text = line.Text;
            var parts = text.Split(' ', 2);
            if (parts.Length == 2 && parts[0] is "PUSH16" or "JMP" or "CALL"
                && int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int operand)
                && SymbolAt(operand) is { } sym)
            {
                text = $"{parts[0]} {parts[1]} ({sym})";
            }
            return $"{text}  [stack: {OperandStackCount}]";
        }
    }

    /// <summary>
    /// The addresses of the call sites of the function/label at
    /// <paramref name="address"/> (cross-references, from the disassembly).
    /// </summary>
    public IReadOnlyList<int> CallReferencesTo(int address) =>
        _disassembly?.CallReferences.TryGetValue(address, out var refs) is true ? refs : [];

    /// <summary>The addresses of branches targeting <paramref name="address"/>.</summary>
    public IReadOnlyList<int> BranchReferencesTo(int address) =>
        _disassembly?.BranchReferences.TryGetValue(address, out var refs) is true ? refs : [];

    #endregion

    /// <summary>Raised after any state change; the shell re-renders on this.</summary>
    public event Action? Changed;

    private void Notify() => Changed?.Invoke();

    /// <summary>Console input queue for the session's machine.</summary>
    private sealed class SessionInputSource : IInputSource
    {
        private readonly Queue<byte> _queue = new();

        public void Enqueue(byte value) => _queue.Enqueue(value);

        public void Clear() => _queue.Clear();

        public bool TryReadByte(out byte value)
        {
            if (_queue.Count == 0)
            {
                value = 0;
                return false;
            }
            value = _queue.Dequeue();
            return true;
        }
    }

    /// <summary>Console output sink for the session's machine.</summary>
    private sealed class SessionOutputSink : IOutputSink
    {
        private readonly StringBuilder _text = new();
        private readonly DebugSession _owner;

        public SessionOutputSink(DebugSession owner) => _owner = owner;

        public string Text => _text.ToString();

        public void Clear() => _text.Clear();

        public void WriteByte(byte value)
        {
            _text.Append((char)value);
            _owner.OnOutputAppended();
        }
    }
}
