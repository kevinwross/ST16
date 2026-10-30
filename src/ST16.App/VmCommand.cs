using ST16.VM;

namespace ST16.App;

/// <summary>
/// <c>st16 vm &lt;file.bin&gt; [--limit &lt;n&gt;</c> (plan P2): run a little-endian
/// word image on the VM with the real console wired to the I/O ports.
/// Exit codes: 0 = HALT, 1 = FAULT, 2 = usage/read error, 3 = limit reached.
/// Program console output goes to stdout untouched; diagnostics go to stderr.
/// </summary>
internal static class VmCommand
{
    public static int Run(string[] args)
    {
        // Grammar: <file.bin> [--limit <n>]
        string file;
        long limit = 0; // 0 = unlimited
        switch (args.Length)
        {
            case 0:
                Console.Error.WriteLine("error: 'vm' needs a <file.bin> argument");
                PrintUsageToStderr();
                return 2;
            case 1:
                file = args[0];
                break;
            case 3 when args[1] == "--limit":
                file = args[0];
                if (!Numbers.TryParse(args[2], out long n))
                {
                    Console.Error.WriteLine($"error: invalid --limit value '{args[2]}'");
                    PrintUsageToStderr();
                    return 2;
                }
                limit = n;
                break;
            default:
                Console.Error.WriteLine("error: unknown or missing argument for 'vm' (expected: <file.bin> [--limit <n>])");
                PrintUsageToStderr();
                return 2;
        }

        byte[] image;
        try
        {
            image = File.ReadAllBytes(file);
        }
        catch (Exception ex)
        {
            // Any read failure (missing file, access denied, invalid path, …) is a usage error.
            Console.Error.WriteLine($"error: could not read '{file}': {ex.Message}");
            return 2;
        }

        if (image.Length % 2 != 0)
        {
            Console.Error.WriteLine($"error: '{file}' has an odd byte count ({image.Length}); ST16 images are little-endian 16-bit words");
            return 2;
        }
        int words = image.Length / 2;
        if (words > Memory.RamSize)
        {
            Console.Error.WriteLine($"error: '{file}' is {words} words; the RAM region holds at most {Memory.RamSize}");
            return 2;
        }

        // Real console wiring: stdin → 0xF001 (non-blocking, Peek-based), stdout ← 0xF000.
        var machine = new ST16Machine(new ConsoleInputSource(Console.In), new ConsoleOutputSink(Console.Out));
        machine.Load(image);
        if (limit != 0)
            machine.InstructionLimit = limit;

        machine.Run();
        Console.Out.Flush();

        switch (machine.State)
        {
            case MachineState.Halted:
                return 0;
            case MachineState.Faulted:
                Console.Error.WriteLine(
                    $"FAULT {FaultCodeNames.Name(machine.LastFault)} at 0x{machine.FaultPc:X4} (instruction {machine.InstructionCount})");
                return 1;
            default:
                // No breakpoints exist in vm mode, so a paused stop here means the limit.
                Console.Error.WriteLine($"instruction limit reached ({machine.InstructionCount} instructions)");
                return 3;
        }
    }

    private static void PrintUsageToStderr() => Program.PrintUsage(Console.Error);
}
