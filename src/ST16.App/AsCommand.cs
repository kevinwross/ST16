using ST16.Assembler;

namespace ST16.App;

/// <summary>
/// <c>st16 as &lt;in.st16&gt; [-o out.bin] [-d out.st16d]</c> (plan P3): assemble a
/// source file to a little-endian binary image and a <c>.st16d</c> v1 debug-symbol
/// file. Defaults: the input's directory, <c>&lt;name&gt;.bin</c> / <c>&lt;name&gt;.st16d</c>.
/// Exit codes: 0 assembled, 1 assembly errors (diagnostics on stderr), 2 usage.
/// </summary>
internal static class AsCommand
{
    public static int Run(string[] args)
    {
        string? input = null;
        string? outputBin = null;
        string? outputSt16d = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                    if (i + 1 >= args.Length)
                        return UsageError("-- '-o' needs a <file> argument");
                    outputBin = args[++i];
                    break;
                case "-d":
                    if (i + 1 >= args.Length)
                        return UsageError("-- '-d' needs a <file> argument");
                    outputSt16d = args[++i];
                    break;
                default:
                    if (input is not null)
                        return UsageError($"unexpected extra argument '{args[i]}'");
                    input = args[i];
                    break;
            }
        }

        if (input is null)
            return UsageError("error: 'as' needs an input <file.st16> argument");

        string text;
        try
        {
            text = File.ReadAllText(input);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not read '{input}': {ex.Message}");
            return 2;
        }

        var result = St16Assembler.Assemble(text, Path.GetFileName(input));

        foreach (var d in result.Diagnostics)
            Console.Error.WriteLine($"error: {Path.GetFileName(input)} {d}");

        if (!result.Success)
            return 1;

        string dir = Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".";
        string name = Path.GetFileNameWithoutExtension(input);
        string binPath = outputBin ?? Path.Combine(dir, name + ".bin");
        string st16dPath = outputSt16d ?? Path.Combine(dir, name + ".st16d");

        File.WriteAllBytes(binPath, result.ToBinary());
        File.WriteAllText(st16dPath, result.DebugSymbols!.ToJson());

        Console.WriteLine($"assembled {result.Words.Count} words → {binPath}");
        Console.WriteLine($"debug symbols → {st16dPath}");
        return 0;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("usage: st16 as <in.st16> [-o out.bin] [-d out.st16d]");
        return 2;
    }
}
