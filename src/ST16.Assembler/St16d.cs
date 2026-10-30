using System.Text.Json;

namespace ST16.Assembler;

/// <summary>One embedded input file (spec: sources[] — full text embedded).</summary>
public sealed class St16dSource
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>One emitted word → (file, 1-based line) mapping (spec: lines[]).</summary>
public sealed class St16dLine
{
    /// <summary>Index into <see cref="St16dDebugFile.Sources"/>.</summary>
    public int File { get; set; }

    /// <summary>1-based source line number.</summary>
    public int Line { get; set; }

    /// <summary>Absolute word address of this emitted word.</summary>
    public int Address { get; set; }
}

/// <summary>
/// A symbol (spec: symbols[]). Kind is "label" (every label), "function"
/// (labels the function heuristic classifies as entry points) or "data".
/// </summary>
public sealed class St16dSymbol
{
    public string Name { get; set; } = "";
    public int Address { get; set; }
    public string Kind { get; set; } = "label";
}

/// <summary>
/// A function entry (spec: functions[]). End is one past the last word of
/// the function: the address of the next label, or origin + total length.
/// </summary>
public sealed class St16dFunction
{
    public string Name { get; set; } = "";
    public int Entry { get; set; }
    public int End { get; set; }
}

/// <summary>One contiguous run of emitted words; a .org gap starts a new section.</summary>
public sealed class St16dSection
{
    public int Address { get; set; }
    public int Length { get; set; }
}

/// <summary>
/// The .st16d v1 debug file: a self-contained, JSON-serialized mapping of the
/// assembled binary back to its sources, labels, functions and sections.
/// Property names are serialized exactly as declared.
/// </summary>
public sealed class St16dDebugFile
{
    public string Format { get; set; } = "st16d";
    public int Version { get; set; } = 1;

    /// <summary>Position counter at the first emitted word (after any .org).</summary>
    public int Origin { get; set; }

    public List<St16dSection> Sections { get; set; } = new();
    public List<St16dSource> Sources { get; set; } = new();
    public List<St16dLine> Lines { get; set; } = new();
    public List<St16dSymbol> Symbols { get; set; } = new();
    public List<St16dFunction> Functions { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Writes this debug file to <paramref name="path"/> as indented JSON.</summary>
    public void WriteJson(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Reads a .st16d file written by <see cref="WriteJson"/>.</summary>
    public static St16dDebugFile ReadJson(string path) =>
        JsonSerializer.Deserialize<St16dDebugFile>(File.ReadAllText(path))
        ?? throw new InvalidDataException($"'{path}' does not contain a .st16d document.");
}
