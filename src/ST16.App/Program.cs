namespace ST16.App;

/// <summary>
/// Entry point of the <c>st16</c> executable (plan P2): subcommand routing
/// plus the global flags. P2 delivers <c>vm</c> and <c>cli</c>; the no-arg
/// default (the debug GUI) lands in Phase 5.
/// </summary>
internal static class Program
{
    public const string Version = "st16 0.1.0 (P2)";

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            // No command: the debug GUI is the no-arg default mode, but it lands in Phase 5.
            Console.WriteLine(Version);
            Console.WriteLine("notice: the debug GUI is the no-argument default mode, but it lands in Phase 5.");
            Console.WriteLine();
            PrintUsage(Console.Out);
            return 2;
        }

        switch (args[0])
        {
            case "--help" or "-h" or "help":
                PrintUsage(Console.Out);
                return 0;
            case "--version" or "-v":
                Console.WriteLine(Version);
                return 0;
            case "vm":
                return VmCommand.Run(args[1..]);
            case "cli":
                return CliCommand.Run(args[1..]);
            case "as":
                return AsCommand.Run(args[1..]);
            case "ds":
                return DsCommand.Run(args[1..]);
            case "cc":
                return CcCommand.Run(args[1..]);
            default:
                Console.Error.WriteLine($"error: unknown command '{args[0]}'");
                PrintUsage(Console.Error);
                return 2;
        }
    }

    public static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("usage: st16 <command> [options]");
        writer.WriteLine();
        writer.WriteLine("commands:");
        writer.WriteLine("  vm <file.bin> [--limit <n>]   run a binary image on the VM; stdin/stdout");
        writer.WriteLine("                               are wired to the console ports (0xF000/0xF001)");
        writer.WriteLine("  cli [file.bin]                interactive debug REPL on stdin/stdout");
        writer.WriteLine("  as <in.st16> [-o out.bin] [-d out.st16d]");
        writer.WriteLine("                                assemble to a binary image + .st16d debug symbols");
        writer.WriteLine("  ds <out.bin> [-d in.st16d]    disassemble to annotated assembly (stdout)");
        writer.WriteLine("  cc <prog.c> [-o out.s]        compile the ST16 C core subset to .s/.bin/.st16d");
        writer.WriteLine("  help, --help                  show this help and exit");
        writer.WriteLine("  --version, -v                 print the version and exit");
        writer.WriteLine();
        writer.WriteLine("with no command the debug GUI would launch (it lands in Phase 5).");
        writer.WriteLine("numbers are decimal, 0x…/0X… hex, or 0b…/0B… binary.");
    }
}
