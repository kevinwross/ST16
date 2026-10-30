using ST16.Assembler;
using ST16.VM;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("ST16: no subcommand — GUI arrives in phase 5 (run `st16 --help`)");
            return 1;
        }

        string cmd = args[0];
        var rest = args.Skip(1).ToArray();

        if (cmd is "--help" or "-h" or "help") return Help();

        switch (cmd)
        {
            case "vm": return Vm(rest);
            case "cli": return Cli(rest);
            case "as": return Assemble(rest);
            case "ds": return Disassemble(rest);
            case "cc":
                Console.Error.WriteLine($"{cmd}: not yet implemented (C compiler: phase 3.5)");
                return 1;
            default:
                Console.Error.WriteLine($"ST16: unknown subcommand '{cmd}' (run `st16 --help`)");
                return 2;
        }
    }

    private static int Help()
    {
        Console.WriteLine("""
            ST16 — 16-bit stack-based virtual CPU (ISA v1.0)

            Usage:
              st16 vm <file.bin> [--limit N]   Run a binary image (little-endian u16 words at 0x0000), console I/O.
              st16 cli [file.bin]              Interactive debugger REPL.
              st16 as <in.st16> [-o out.bin] [-d out.st16d]
                                              Assemble source to binary + debug file.
              st16 ds <in.bin> [-d in.st16d]   Disassemble a binary (labels from .st16d when given).
              st16 cc <in.c> -o <out.st16>     Compile C to ST16 assembly (phase 3.5 — not yet implemented)
              st16                             (default; GUI arrives in phase 5)

            Exit codes: 0 ok · 1 fault / runtime error · 2 usage · 3 instruction limit reached
            """);
        return 0;
    }

    // ---------------- st16 vm ----------------

    private static int Vm(string[] args)
    {
        string? file = null;
        uint limit = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--limit" && i + 1 < args.Length)
            {
                if (!uint.TryParse(args[++i], out limit))
                {
                    Console.Error.WriteLine("vm: bad --limit value (use a non-negative integer)");
                    return 2;
                }
            }
            else if (args[i] is "--help" or "-h")
            {
                Console.WriteLine("Usage: st16 vm <file.bin> [--limit N]");
                return 0;
            }
            else if (file is null && !args[i].StartsWith("--"))
            {
                file = args[i];
            }
            else
            {
                Console.Error.WriteLine($"vm: unexpected argument '{args[i]}'");
                return 2;
            }
        }
        if (file is null)
        {
            Console.Error.WriteLine("Usage: st16 vm <file.bin> [--limit N]");
            return 2;
        }

        var (words, error) = TryReadImage(file);
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var m = new ST16Machine(new ST16Config { InstructionLimit = limit }, new ConsoleInputSource(), new ConsoleOutputSink());
        if (!m.LoadProgram(words))
        {
            Console.Error.WriteLine("vm: program exceeds RAM (0x0000–0xEFFF)");
            return 2;
        }

        m.Run();
        return Report(m);
    }

    private static int Report(ST16Machine m) => m.State switch
    {
        MachineState.Halted => PrintAndReturn($"HALT ok ({m.InstructionCount} instructions)", 0),
        MachineState.Faulted => PrintAndReturn($"FAULT {m.FaultCode} at PC=0x{m.FaultPC:X4}", 1),
        MachineState.Paused when m.StopReason == StopReason.LimitReached =>
            PrintAndReturn($"LIMIT reached after {m.InstructionCount} instructions", 3),
        _ => PrintAndReturn($"stopped ({m.State}/{m.StopReason}) after {m.InstructionCount} instructions", 1),
    };

    private static int PrintAndReturn(string text, int code)
    {
        Console.WriteLine(text);
        return code;
    }

    // ---------------- st16 as ----------------

    private static int Assemble(string[] args)
    {
        string? input = null, binOut = null, debugOut = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "-o" or "--output")
            {
                if (++i >= args.Length) return UsageAs();
                binOut = args[i];
            }
            else if (args[i] is "-d" or "--debug")
            {
                if (++i >= args.Length) return UsageAs();
                debugOut = args[i];
            }
            else if (args[i] is "--help" or "-h")
            {
                Console.WriteLine("Usage: st16 as <in.st16> [-o out.bin] [-d out.st16d]");
                return 0;
            }
            else if (input is null && !args[i].StartsWith("-"))
            {
                input = args[i];
            }
            else
            {
                Console.Error.WriteLine($"as: unexpected argument '{args[i]}'");
                return 2;
            }
        }
        if (input is null) return UsageAs();
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"as: cannot read '{input}'");
            return 2;
        }

        string text = File.ReadAllText(input);
        var result = Assembler.Assemble(Path.GetFileName(input), text);

        foreach (var d in result.Diagnostics)
            Console.Error.WriteLine($"{input} {d}");

        if (!result.Success)
        {
            int errors = result.Diagnostics.Count(d => d.Level == "error");
            Console.Error.WriteLine($"{input}: assembly failed ({errors} error(s))");
            return 1;
        }

        string baseName = Path.Combine(
            Path.GetDirectoryName(input) ?? ".",
            Path.GetFileNameWithoutExtension(input));
        string bin = binOut ?? baseName + ".bin";
        string dbg = debugOut ?? baseName + ".st16d";

        try
        {
            result.WriteBin(bin);
            result.WriteDebug(dbg);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"as: cannot write output ({ex.Message})");
            return 2;
        }

        Console.WriteLine($"assembled {result.Words.Length} words -> {bin} (+ {dbg})");
        return 0;
    }

    private static int UsageAs()
    {
        Console.Error.WriteLine("Usage: st16 as <in.st16> [-o out.bin] [-d out.st16d]");
        return 2;
    }

    // ---------------- st16 ds ----------------

    private static int Disassemble(string[] args)
    {
        string? input = null, debugIn = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "-d" or "--debug")
            {
                if (++i >= args.Length)
                {
                    Console.Error.WriteLine("Usage: st16 ds <in.bin> [-d in.st16d]");
                    return 2;
                }
                debugIn = args[i];
            }
            else if (args[i] is "--help" or "-h")
            {
                Console.WriteLine("Usage: st16 ds <in.bin> [-d in.st16d]");
                return 0;
            }
            else if (input is null && !args[i].StartsWith("-"))
            {
                input = args[i];
            }
            else
            {
                Console.Error.WriteLine($"ds: unexpected argument '{args[i]}'");
                return 2;
            }
        }
        if (input is null)
        {
            Console.Error.WriteLine("Usage: st16 ds <in.bin> [-d in.st16d]");
            return 2;
        }

        var (words, error) = TryReadImage(input);
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        St16dDebugFile? debug = null;
        if (debugIn is not null)
        {
            try
            {
                debug = St16dDebugFile.ReadJson(debugIn);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ds: cannot read '{debugIn}' ({ex.Message})");
                return 2;
            }
        }

        var dis = Disassembler.Disassemble(words, 0, debug);
        foreach (var d in dis.Diagnostics)
            Console.Error.WriteLine($"{input} {d}");
        Console.Write(dis.Assembly);
        return 0;
    }

    // ---------------- st16 cli ----------------

    private sealed class CliOutputSink : IOutputSink
    {
        public void WriteChar(byte b) => Console.WriteLine($"[out] {(char)b}");
    }

    private static int Cli(string[] args)
    {
        string? file = null;
        foreach (string a in args)
            if (!a.StartsWith("--")) file ??= a;

        var input = new InMemoryInput(Array.Empty<byte>());
        var m = new ST16Machine(new ST16Config(), input, new CliOutputSink());

        if (file is not null && !TryLoad(m, file)) return 2;
        Console.WriteLine("ST16 cli — `help` for commands, `exit` to quit.");

        while (true)
        {
            Console.Write("st16> ");
            string? line = Console.ReadLine();
            if (line is null) break;                       // EOF
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line is "exit" or "quit") break;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string cmd = parts[0].ToLowerInvariant();
            try
            {
                switch (cmd)
                {
                    case "help":
                        CliHelp();
                        break;

                    case "load":
                        if (parts.Length < 2 || !TryLoad(m, parts[1]))
                            Console.WriteLine("usage: load <file.bin>");
                        break;

                    case "run":
                        m.Run();
                        Announce(m);
                        break;

                    case "step":
                    {
                        string mode = parts.Length > 1 ? parts[1].ToLowerInvariant() : "into";
                        if (mode is not ("into" or "over" or "out"))
                        {
                            Console.WriteLine("usage: step [into|over|out]");
                            break;
                        }
                        bool stepped = mode switch
                        {
                            "into" => m.Step(),
                            "over" => m.StepOver(),
                            _ => m.StepOut(),
                        };
                        if (!stepped) Console.WriteLine("machine is not running (load a program or resume)");
                        else Announce(m);
                        break;
                    }

                    case "state":
                        Console.WriteLine($"state={m.State}  reason={m.StopReason}  PC=0x{m.PC:X4}  " +
                            $"insns={m.InstructionCount}  fault={m.FaultCode}  faultPC=0x{m.FaultPC:X4}  " +
                            $"operand={m.OperandStack.Count}/{m.Config.OperandStackDepth}  " +
                            $"call={m.CallStack.Count}/{m.Config.CallStackDepth}  limit={m.InstructionLimit}");
                        break;

                    case "dump":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("usage: dump stack | memory [addr] [count] | callstack");
                            break;
                        }
                        switch (parts[1].ToLowerInvariant())
                        {
                            case "stack":
                                if (m.OperandStack.Count == 0) Console.WriteLine("(empty)");
                                else for (int i = 0; i < m.OperandStack.Count; i++)
                                    Console.WriteLine($"  s{i + 1,-2}  0x{m.OperandStack[i]:X4}  ({(int)(short)m.OperandStack[i]:6})" +
                                                      (i == 0 ? "   (top)" : ""));
                                break;

                            case "memory":
                            {
                                ushort addr = 0;
                                int count = 16;
                                if (parts.Length > 2 && !ParseAddr(parts[2], out addr))
                                {
                                    Console.WriteLine("addr must be decimal or 0x hex");
                                    break;
                                }
                                if (parts.Length > 3 && !int.TryParse(parts[3], out count) || count <= 0)
                                {
                                    Console.WriteLine("count must be a positive integer");
                                    break;
                                }
                                for (int i = 0; i < count; i++)
                                {
                                    ushort a = (ushort)(addr + i);
                                    if (a >= ST16Machine.PortOut)
                                    {
                                        Console.WriteLine($"  0x{a:X4}: <port>");
                                        continue;
                                    }
                                    Console.WriteLine($"  0x{a:X4}: 0x{m.ReadMemory(a):X4}  ({(int)(short)m.ReadMemory(a):6})");
                                }
                                break;
                            }

                            case "callstack":
                                if (m.CallStack.Count == 0) Console.WriteLine("(empty)");
                                else for (int i = 0; i < m.CallStack.Count; i++)
                                    Console.WriteLine($"  f{i + 1,-2}  return to 0x{m.CallStack[i]:X4}");
                                break;

                            default:
                                Console.WriteLine("usage: dump stack | memory [addr] [count] | callstack");
                                break;
                        }
                        break;

                    case "bp":
                        if (parts.Length < 2)
                        {
                            Console.WriteLine("usage: bp <addr> | bp off <addr> | bp fault | bp fault off");
                            break;
                        }
                        if (parts[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                        {
                            if (parts.Length < 3 || !ParseAddr(parts[2], out ushort off))
                            {
                                Console.WriteLine("usage: bp off <addr>");
                                break;
                            }
                            m.ClearBreakpoint(off);
                            Console.WriteLine($"breakpoint cleared at 0x{off:X4}");
                        }
                        else if (parts[1].Equals("fault", StringComparison.OrdinalIgnoreCase))
                        {
                            bool on = parts.Length < 3 || !parts[2].Equals("off", StringComparison.OrdinalIgnoreCase);
                            m.BreakOnFaultEnabled = on;
                            Console.WriteLine($"break-on-fault: {(on ? "on" : "off")} (v1: faults are always terminal)");
                        }
                        else
                        {
                            if (!ParseAddr(parts[1], out ushort addr))
                            {
                                Console.WriteLine("addr must be decimal or 0x hex");
                                break;
                            }
                            m.SetBreakpoint(addr);
                            Console.WriteLine($"breakpoint set at 0x{addr:X4}");
                        }
                        break;

                    case "limit":
                        if (parts.Length < 2 || !uint.TryParse(parts[1], out uint n))
                        {
                            Console.WriteLine("usage: limit <n>  (0 = unlimited)");
                            break;
                        }
                        m.InstructionLimit = n;
                        Console.WriteLine(n == 0 ? "limit: unlimited" : $"limit: {n} instructions");
                        break;

                    case "in":
                    {
                        int i = 0;
                        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                        i += 2;                                   // skip "in"
                        while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                        string text = line[i..];
                        foreach (char ch in text) input.Enqueue((byte)ch);
                        Console.WriteLine($"queued {text.Length} character(s) for port 0xF001");
                        break;
                    }

                    default:
                        Console.WriteLine($"unknown command '{cmd}' (try `help`)");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error: {ex.Message}");
            }
        }
        return 0;
    }

    private static void Announce(ST16Machine m)
    {
        switch (m.State)
        {
            case MachineState.Running:
                Console.WriteLine($"running  PC=0x{m.PC:X4}  insns={m.InstructionCount}");
                break;
            case MachineState.Paused:
                Console.WriteLine($"paused ({m.StopReason})  PC=0x{m.PC:X4}  insns={m.InstructionCount}");
                break;
            case MachineState.Faulted:
                Console.WriteLine($"FAULT {m.FaultCode} at PC=0x{m.FaultPC:X4}");
                break;
            case MachineState.Halted:
                Console.WriteLine($"HALT ok ({m.InstructionCount} instructions)");
                break;
        }
    }

    private static void CliHelp()
    {
        Console.WriteLine("""
            Commands:
              load <file.bin>            Load a program (resets the machine)
              run                        Run until stop (breakpoint / fault / limit / HALT)
              step [into|over|out]       Execute one instruction / one call / out of the frame
              state                      Show machine state
              dump stack                 Operand stack (top first)
              dump memory [addr] [count] Dump RAM words (default 0x0000, 16 words)
              dump callstack             Call stack (most recent first)
              bp <addr>                  Set breakpoint (decimal or 0x hex)
              bp off <addr>              Clear breakpoint
              bp fault [off]             Toggle fault reporting
              limit <n>                  Instruction limit (0 = unlimited)
              in <text>                  Queue characters for console input (port 0xF001)
              help                       This help
              exit | quit                Leave the CLI
            """);
    }

    private static bool ParseAddr(string s, out ushort addr)
    {
        addr = 0;
        int v;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (s.Length < 3 || !int.TryParse(s[2..], System.Globalization.NumberStyles.HexNumber, null, out v))
                return false;
        }
        else if (!int.TryParse(s, out v)) return false;
        if (v < 0 || v > 0xFFFF) return false;
        addr = (ushort)v;
        return true;
    }

    private static (ushort[] Words, string? Error) TryReadImage(string file)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Length % 2 != 0)
                return (Array.Empty<ushort>(), $"{file}: image length must be an even number of bytes");
            var words = new ushort[bytes.Length / 2];
            for (int i = 0; i < words.Length; i++)
                words[i] = (ushort)(bytes[2 * i] | (bytes[2 * i + 1] << 8));   // little-endian
            return (words, null);
        }
        catch (Exception ex)
        {
            return (Array.Empty<ushort>(), $"{file}: cannot read ({ex.Message})");
        }
    }

    private static bool TryLoad(ST16Machine m, string file)
    {
        var (words, error) = TryReadImage(file);
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            return false;
        }
        if (!m.LoadProgram(words))
        {
            Console.Error.WriteLine($"{file}: program exceeds RAM (0x0000–0xEFFF)");
            return false;
        }
        Console.WriteLine($"loaded {words.Length} words from {file} at 0x0000");
        return true;
    }
}
