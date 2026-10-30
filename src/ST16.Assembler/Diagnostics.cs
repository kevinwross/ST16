namespace ST16.Assembler;

/// <summary>One input source file: its name and full text.</summary>
public sealed record SourceFile(string Name, string Text);

/// <summary>
/// A line/column-anchored diagnostic (spec §8.4: "Line-numbered diagnostics").
/// Level is "error" or "warning".
/// </summary>
public sealed class Diagnostic
{
    public string Level { get; }
    public string Message { get; }
    public int Line { get; }
    public int Column { get; }

    public Diagnostic(string level, string message, int line, int column)
    {
        Level = level;
        Message = message;
        Line = line;
        Column = column;
    }

    public override string ToString() => $"{Line}:{Column}: {Level}: {Message}";
}

/// <summary>
/// Result of <see cref="Assembler.Assemble"/>: the word image (empty on failure),
/// the .st16d debug file (populated on success), and all collected diagnostics.
/// </summary>
public sealed class AssembleResult
{
    public bool Success { get; }

    /// <summary>Assembled words; empty when <see cref="Success"/> is false.</summary>
    public ushort[] Words { get; }

    /// <summary>Populated on success; null when assembly failed.</summary>
    public St16dDebugFile Debug { get; }

    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    internal AssembleResult(
        bool success,
        ushort[] words,
        St16dDebugFile debug,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        Success = success;
        Words = words;
        Debug = debug;
        Diagnostics = diagnostics;
    }

    /// <summary>Writes the word image as little-endian 16-bit words (spec §8: "little-endian byte pairs").</summary>
    public void WriteBin(string path)
    {
        if (!Success)
            throw new InvalidOperationException("Assembly failed; there is no binary to write.");

        var bytes = new byte[Words.Length * 2];
        for (int i = 0; i < Words.Length; i++)
        {
            bytes[2 * i] = (byte)(Words[i] & 0xFF);
            bytes[2 * i + 1] = (byte)(Words[i] >> 8);
        }

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Writes the .st16d debug file as indented JSON.</summary>
    public void WriteDebug(string path)
    {
        if (Debug is null)
            throw new InvalidOperationException("Assembly failed; there is no debug file to write.");

        Debug.WriteJson(path);
    }
}
