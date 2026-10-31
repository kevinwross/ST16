using ST16.CC;

namespace ST16.App;

/// <summary>
/// <c>st16 cc &lt;prog.c&gt; [-o out.s]</c> (plan P3.5/P4): compile a C file in
/// the ST16 core subset to ST16 assembly, a binary image, and a post-processed
/// <c>.st16d</c> (sources = the .c file, lines → .c). Defaults: the input's
/// directory, <c>&lt;name&gt;.s</c> / <c>&lt;name&gt;.bin</c> / <c>&lt;name&gt;.st16d</c>.
/// Exit codes: 0 compiled, 1 compile error (diagnostic on stderr), 2 usage.
/// </summary>
internal static class CcCommand
{
    public static int Run(string[] args)
    {
        string? input = null;
        string? outputAsm = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                    if (i + 1 >= args.Length)
                        return UsageError("-- '-o' needs a <file> argument");
                    outputAsm = args[++i];
                    break;
                default:
                    if (input is not null)
                        return UsageError($"unexpected extra argument '{args[i]}'");
                    input = args[i];
                    break;
            }
        }

        if (input is null)
            return UsageError("error: 'cc' needs an input <file.c> argument");

        string source;
        try
        {
            source = File.ReadAllText(input);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not read '{input}': {ex.Message}");
            return 2;
        }

        CCCompiler.Output output;
        try
        {
            output = CCCompiler.Compile(source, input);
        }
        catch (CCException ex)
        {
            Console.Error.WriteLine($"{Path.GetFileName(input)}: {ex.Diagnostic}");
            return 1;
        }

        string dir = Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".";
        string name = Path.GetFileNameWithoutExtension(input);
        string asmPath = outputAsm ?? Path.Combine(dir, name + ".s");
        string asmDir = Path.GetDirectoryName(Path.GetFullPath(asmPath)) ?? ".";
        string asmName = Path.GetFileNameWithoutExtension(asmPath);

        File.WriteAllText(asmPath, output.Asm);
        File.WriteAllBytes(Path.Combine(asmDir, asmName + ".bin"), output.Binary);
        File.WriteAllText(Path.Combine(asmDir, asmName + ".st16d"), output.St16d.ToJson());

        Console.WriteLine($"compiled {Path.GetFileName(input)} → {output.Assembly.Words.Count} words → {asmPath}");
        Console.WriteLine($"binary → {Path.Combine(asmDir, asmName + ".bin")}");
        Console.WriteLine($"debug symbols → {Path.Combine(asmDir, asmName + ".st16d")}");
        return 0;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("usage: st16 cc <prog.c> [-o out.s]");
        return 2;
    }
}
