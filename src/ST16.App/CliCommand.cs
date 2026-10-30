using ST16.VM;

namespace ST16.App;

/// <summary>
/// <c>st16 cli [file.bin]</c> (plan P2): the interactive debug REPL on
/// stdin/stdout. All host messages go to stdout; the VM's own program output
/// also goes to stdout, which is expected and fine for P2.
/// </summary>
internal sealed class CliCommand
{
    private readonly ST16Machine _machine;
    private readonly QueueInputSource _input;
    private bool _exiting;

    /// <summary>Entry point for the <c>cli</c> subcommand; returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        if (args.Length > 1)
        {
            Console.Error.WriteLine("error: 'cli' takes at most one argument ([file.bin])");
            Program.PrintUsage(Console.Error);
            return 2;
        }

        var repl = new CliCommand();
        if (args.Length == 1)
        {
            if (!repl.LoadFile(args[0], out int words))
                return 2; // "error: …" already reported on stderr.
            repl.Write($"loaded {words} words\n");
            repl.Flush();
        }

        repl.Write("st16 cli 0.1.0 (P2) — type 'help' for commands, 'exit' to leave\n");
        repl.Flush();
        return repl.Loop();
    }

    private CliCommand()
    {
        // 0xF001 is fed from the queue; the `in` command adds bytes to it.
        _input = new QueueInputSource();
        _machine = new ST16Machine(_input, new ConsoleOutputSink(Console.Out));
    }

    #region REPL loop

    private int Loop()
    {
        while (true)
        {
            Write("st16> ");
            Flush();

            string? line = Console.ReadLine();
            if (line is null) // EOF: a finite input sequence ends here.
            {
                Write("bye\n");
                Flush();
                return 0;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            Execute(trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            Flush();
            if (_exiting)
                return 0;
        }
    }

    private void Execute(string[] tokens)
    {
        switch (tokens[0].ToLowerInvariant())
        {
            case "load": CmdLoad(tokens); break;
            case "run": CmdRun(); break;
            case "step": CmdStep(tokens); break;
            case "dump": CmdDump(tokens); break;
            case "bp": CmdBreakpoint(tokens); break;
            case "limit": CmdLimit(tokens); break;
            case "in": CmdIn(tokens); break;
            case "state": CmdState(); break;
            case "help": CmdHelp(); break;
            case "exit":
            case "quit":
                Write("bye\n");
                _exiting = true;
                break;
            default:
                Write($"unknown command: '{tokens[0]}' — try 'help'\n");
                break;
        }
    }

    #endregion

    #region Commands

    private void CmdLoad(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Write("error: usage: load <file.bin>\n");
            return;
        }
        if (LoadFile(tokens[1], out int words))
            Write($"loaded {words} words\n");
    }

    /// <summary>Loads a little-endian word image (which resets the machine); reports to stderr and returns false on error.</summary>
    private bool LoadFile(string path, out int words)
    {
        words = 0;
        byte[] image;
        try
        {
            image = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            // Any read failure (missing file, access denied, invalid path, …) is a load error.
            Console.Error.WriteLine($"error: could not read '{path}': {ex.Message}");
            return false;
        }

        if (image.Length % 2 != 0)
        {
            Console.Error.WriteLine($"error: '{path}' has an odd byte count ({image.Length}); ST16 images are little-endian 16-bit words");
            return false;
        }
        words = image.Length / 2;
        if (words > Memory.RamSize)
        {
            Console.Error.WriteLine($"error: '{path}' is {words} words; the RAM region holds at most {Memory.RamSize}");
            return false;
        }

        _machine.Load(image); // Load resets the machine to the spec §2.5 initial state.
        return true;
    }

    private void CmdRun()
    {
        if (_machine.State is MachineState.Faulted or MachineState.Halted)
        {
            Write($"machine already {_machine.State.ToString().ToLowerInvariant()}\n");
            return;
        }

        _machine.Run();
        Write(StopReasonLine() + "\n");
    }

    /// <summary>The exact one-line stop reason for the most recent run (plan P2 formats).</summary>
    private string StopReasonLine()
    {
        return _machine.State switch
        {
            MachineState.Halted => "halted (termination code 0)",
            MachineState.Faulted =>
                $"FAULT {FaultCodeNames.Name(_machine.LastFault)} at 0x{_machine.FaultPc:X4} (instruction {_machine.InstructionCount})",
            // A paused stop here is a breakpoint or the instruction-limit watchdog.
            _ => _machine.LastRunStop == RunStopReason.Breakpoint
                ? $"breakpoint at 0x{_machine.Pc:X4}"
                : $"instruction limit reached ({_machine.InstructionCount} instructions)",
        };
    }

    private void CmdStep(string[] tokens)
    {
        if (tokens.Length == 1)
        {
            _machine.StepInto();
        }
        else if (tokens.Length == 2)
        {
            switch (tokens[1].ToLowerInvariant())
            {
                case "into": _machine.StepInto(); break;
                case "over": _machine.StepOver(); break;
                case "out": _machine.StepOut(); break;
                default:
                    Write($"error: unknown step mode '{tokens[1]}' (into, over, or out)\n");
                    return;
            }
        }
        else
        {
            Write("error: usage: step [into|over|out]\n");
            return;
        }

        // Always report the resulting status; when the machine faulted or is
        // halted, the step is a no-op and the line shows the terminal state.
        Write($"pc=0x{_machine.Pc:X4} state={_machine.State} ic={_machine.InstructionCount}\n");
    }

    private void CmdDump(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            Write("error: usage: dump <stack|memory|callstack> [args]\n");
            return;
        }
        switch (tokens[1].ToLowerInvariant())
        {
            case "stack": DumpStack(); break;
            case "memory": DumpMemory(tokens); break;
            case "callstack": DumpCallStack(); break;
            default:
                Write($"error: unknown dump target '{tokens[1]}' (stack, memory, or callstack)\n");
                break;
        }
    }

    private void DumpStack()
    {
        // The machine exposes the full-capacity stack (unoccupied slots read 0,
        // indistinguishable from a real 0), so the occupied slots come from a
        // snapshot: its count plus the top-down copy of the occupied slots.
        MachineSnapshot snap = _machine.TakeSnapshot();
        for (int i = 0; i < snap.OperandCount; i++)
        {
            ushort value = snap.OperandStackTopDown[i];
            Write($"s{i + 1}  0x{value:X4}{(short)value,7}\n");
        }
        Write($"({snap.OperandCount} of {_machine.Config.OperandDepth} occupied)\n");
    }

    private void DumpMemory(string[] tokens)
    {
        // dump memory [start] [count] — defaults: start 0x0000, count 16.
        if (tokens.Length > 4)
        {
            Write("error: usage: dump memory [start] [count]\n");
            return;
        }

        int start = 0;
        if (tokens.Length >= 3)
        {
            if (!Numbers.TryParseAddress(tokens[2], out ushort startAddress))
            {
                Write($"error: '{tokens[2]}' is not a valid address (decimal, 0x… or 0b…; 0–0xFFFF)\n");
                return;
            }
            start = startAddress;
        }

        long count = 16;
        if (tokens.Length >= 4)
        {
            if (!Numbers.TryParse(tokens[3], out long parsed) || parsed < 1)
            {
                Write($"error: '{tokens[3]}' is not a valid word count (positive number)\n");
                return;
            }
            count = parsed;
        }

        if ((long)start + count > Memory.RamSize)
        {
            Write($"error: window 0x{start:X4} + {count} words exceeds the RAM region (0x0000–0xEFFF)\n");
            return;
        }

        ushort[] window = _machine.MemoryWindow((ushort)start, (int)count);
        for (int i = 0; i < window.Length; i += 4)
        {
            Write($"0x{start + i:X4}: ");
            for (int j = 0; j < 4 && i + j < window.Length; j++)
                Write($"{(j == 0 ? "" : " ")}0x{window[i + j]:X4}");
            Write("\n");
        }
    }

    private void DumpCallStack()
    {
        IReadOnlyList<ushort> frames = _machine.CallStack;
        if (frames.Count == 0)
        {
            Write("(empty)\n");
            return;
        }
        for (int i = 0; i < frames.Count; i++)
            Write($"#{i}  0x{frames[i]:X4}\n");
    }

    private void CmdBreakpoint(string[] tokens)
    {
        if (tokens.Length < 2)
        {
            Write("error: usage: bp <set|clear|list>\n");
            return;
        }
        switch (tokens[1].ToLowerInvariant())
        {
            case "set":
            case "clear":
                if (tokens.Length != 3)
                {
                    Write($"error: usage: bp {tokens[1].ToLowerInvariant()} <addr>\n");
                    return;
                }
                if (!Numbers.TryParseAddress(tokens[2], out ushort address))
                {
                    Write($"error: '{tokens[2]}' is not a valid address (decimal, 0x… or 0b…; 0–0xFFFF)\n");
                    return;
                }
                if (tokens[1].ToLowerInvariant() == "set")
                {
                    _machine.SetBreakpoint(address);
                    Write($"breakpoint set at 0x{address:X4}\n");
                }
                else
                {
                    _machine.ClearBreakpoint(address);
                    Write($"breakpoint cleared at 0x{address:X4}\n");
                }
                break;
            case "list":
                if (tokens.Length != 2)
                {
                    Write("error: usage: bp list\n");
                    return;
                }
                IReadOnlyList<ushort> breakpoints = _machine.Breakpoints;
                if (breakpoints.Count == 0)
                {
                    Write("(none)\n");
                }
                else
                {
                    Write("breakpoints: " + string.Join(", ", breakpoints.Select(a => $"0x{a:X4}")) + "\n");
                }
                break;
            default:
                Write($"error: unknown bp subcommand '{tokens[1]}' (set, clear, or list)\n");
                break;
        }
    }

    private void CmdLimit(string[] tokens)
    {
        if (tokens.Length == 1)
        {
            Write(LimitText(_machine.InstructionLimit) + "\n");
            return;
        }
        if (tokens.Length != 2)
        {
            Write("error: usage: limit [n]\n");
            return;
        }
        if (!Numbers.TryParse(tokens[1], out long n))
        {
            Write($"error: '{tokens[1]}' is not a valid limit (non-negative number; 0 = unlimited)\n");
            return;
        }
        _machine.InstructionLimit = n;
        Write(LimitText(n) + "\n");
    }

    private void CmdIn(string[] tokens)
    {
        if (tokens.Length != 2)
        {
            Write("error: usage: in <value> (0–255)\n");
            return;
        }
        if (!Numbers.TryParse(tokens[1], out long n) || n > 0xFF)
        {
            Write($"error: '{tokens[1]}' is not a valid byte value (0–255; decimal, 0x… or 0b…)\n");
            return;
        }
        byte value = (byte)n;
        _input.Enqueue(value);
        char displayed = value is >= 0x20 and <= 0x7E ? (char)value : '?';
        Write($"queued 0x{value:X2} ('{displayed}' )\n");
    }

    private void CmdState()
    {
        Write(
            $"pc=0x{_machine.Pc:X4} state={_machine.State} ic={_machine.InstructionCount} " +
            $"fault={FaultCodeNames.Name(_machine.LastFault)} fault_pc=0x{_machine.FaultPc:X4} " +
            $"last_stop={_machine.LastRunStop}\n");
    }

    private void CmdHelp()
    {
        Write("commands:\n");
        Write("  load <file.bin>         load a little-endian word image (resets the machine)\n");
        Write("  run                     run to a breakpoint, fault, HALT, or the limit\n");
        Write("  step [into|over|out]    step one instruction (into by default; over/out are frame-aware)\n");
        Write("  dump stack              show the occupied operand-stack slots, top first\n");
        Write("  dump memory [s] [n]     show a RAM window, 4 words per line (default 0x0000, 16 words)\n");
        Write("  dump callstack          show the call stack, top frame first (#0 = most recent CALL)\n");
        Write("  bp set <addr>           set a breakpoint\n");
        Write("  bp clear <addr>         clear a breakpoint\n");
        Write("  bp list                 list breakpoints\n");
        Write("  limit [n]               show or set the instruction limit (0 = unlimited)\n");
        Write("  in <value>              queue one console-input byte (0–255)\n");
        Write("  state                   show pc, state, ic, fault, fault_pc, last stop\n");
        Write("  exit, quit              leave the REPL\n");
    }

    #endregion

    #region Output helpers

    private static string LimitText(long limit) => limit == 0 ? "limit: unlimited" : $"limit: {limit}";

    private void Write(string text) => Console.Out.Write(text);

    private void Flush() => Console.Out.Flush();

    #endregion
}
