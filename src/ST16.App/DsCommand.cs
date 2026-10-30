using ST16.Assembler;
using ST16.Disassembler;

namespace ST16.App;

/// <summary>
/// <c>st16 ds &lt;out.bin&gt; [-d in.st16d]</c> (plan P3): disassemble a binary image
/// to annotated assembly on stdout. With a <c>.st16d</c> file, branch targets and
/// PUSH16 values resolve to symbol names. Exit codes: 0 success, 2 usage/read error.
/// </summary>
internal static class DsCommand
{
    public static int Run(string[] args)
    {
        string? input = null;
        string? symbolsFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-d":
                    if (i + 1 >= args.Length)
                        return UsageError("error: '-d' needs a <file> argument");
                    symbolsFile = args[++i];
                    break;
                default:
                    if (input is not null)
                        return UsageError($"error: unexpected extra argument '{args[i]}'");
                    input = args[i];
                    break;
            }
        }

        if (input is null)
            return UsageError("error: 'ds' needs an input <file.bin> argument");

        byte[] image;
        try
        {
            image = File.ReadAllBytes(input);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not read '{input}': {ex.Message}");
            return 2;
        }

        if (image.Length % 2 != 0)
        {
            Console.Error.WriteLine(
                $"error: '{input}' has an odd byte count ({image.Length}); ST16 images are little-endian 16-bit words");
            return 2;
        }

        var words = new ushort[image.Length / 2];
        for (int i = 0; i < words.Length; i++)
            words[i] = (ushort)(image[2 * i] | (image[2 * i + 1] << 8));

        St16dFile? symbols = null;
        if (symbolsFile is not null)
        {
            try
            {
                symbols = St16dFile.FromJson(File.ReadAllText(symbolsFile));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: could not read '{symbolsFile}': {ex.Message}");
                return 2;
            }
        }

        var disassembly = St16Disassembler.Disassemble(words, symbols);
        Console.Write(disassembly.ToText());
        return 0;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("usage: st16 ds <out.bin> [-d in.st16d]");
        return 2;
    }
}
