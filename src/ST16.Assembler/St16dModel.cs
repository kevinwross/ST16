using System.Text.Json.Serialization;

namespace ST16.Assembler;

/// <summary>Severity of an assembler diagnostic.</summary>
public enum Severity
{
    Error,
    Warning,
}

/// <summary>A line/column-numbered assembler diagnostic (spec §8.4).</summary>
public sealed class Diagnostic
{
    public Diagnostic(Severity severity, int line, int column, string message)
    {
        Severity = severity;
        Line = line;
        Column = column;
        Message = message;
    }

    /// <summary>Severity.</summary>
    public Severity Severity { get; }

    /// <summary>One-based line number (0 = the whole file).</summary>
    public int Line { get; }

    /// <summary>One-based column (0 = not applicable).</summary>
    public int Column { get; }

    /// <summary>Message text.</summary>
    public string Message { get; }

    public override string ToString() => $"line {Line}:{Column} {Severity}: {Message}";
}

/// <summary>Kind of a debug symbol (spec/plan: label, function, data).</summary>
public enum SymbolKind
{
    Label,
    Function,
    Data,
}

internal static class SymbolKindJson
{
    public static string ToJson(SymbolKind kind) => kind switch
    {
        SymbolKind.Function => "function",
        SymbolKind.Data => "data",
        _ => "label",
    };

    public static SymbolKind FromJson(string s) => s switch
    {
        "function" => SymbolKind.Function,
        "data" => SymbolKind.Data,
        _ => SymbolKind.Label,
    };
}

/// <summary>A named symbol at an address.</summary>
public sealed record SymbolInfo(string Name, int Address, SymbolKind Kind);

/// <summary>A function: entry address and exclusive end address.</summary>
public sealed record FunctionInfo(string Name, int Entry, int End);

/// <summary>A contiguous section of written words.</summary>
public sealed record SectionInfo(int Origin, int Length);

/// <summary>One source word mapped to a file/line (two-word instructions map the header line to both words).</summary>
public sealed record LineInfo(int Address, int File, int Line);

/// <summary>A source file embedded in a <see cref="St16dFile"/> (name + full text).</summary>
public sealed record SourceFile(string Name, string Text);

/// <summary>
/// The <c>.st16d</c> v1 debug-symbol document (JSON, self-contained: the full source text
/// is embedded so the GUI needs nothing else). Produced by the assembler.
/// </summary>
public sealed class St16dFile
{
    /// <summary>Schema version (1).</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    /// <summary>Embedded source files (index is the <c>file</c> value in <see cref="Lines"/>).</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<SourceFile> Sources { get; init; } = Array.Empty<SourceFile>();

    /// <summary>One entry per written word: address → file index / line number.</summary>
    [JsonPropertyName("lines")]
    public IReadOnlyList<LineInfo> Lines { get; init; } = Array.Empty<LineInfo>();

    /// <summary>Labels, functions (a label targeted by CALL), and data symbols.</summary>
    [JsonPropertyName("symbols")]
    public IReadOnlyList<SymbolInfo> Symbols { get; init; } = Array.Empty<SymbolInfo>();

    /// <summary>Functions with entry and exclusive end addresses.</summary>
    [JsonPropertyName("functions")]
    public IReadOnlyList<FunctionInfo> Functions { get; init; } = Array.Empty<FunctionInfo>();

    /// <summary>Maximal contiguous runs of written words.</summary>
    [JsonPropertyName("sections")]
    public IReadOnlyList<SectionInfo> Sections { get; init; } = Array.Empty<SectionInfo>();

    /// <summary>Serialize to the v1 JSON document.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);

            writer.WritePropertyName("sources");
            writer.WriteStartArray();
            foreach (var s in Sources)
            {
                writer.WriteStartObject();
                writer.WriteString("name", s.Name);
                writer.WriteString("text", s.Text);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("lines");
            writer.WriteStartArray();
            foreach (var l in Lines)
            {
                writer.WriteStartObject();
                writer.WriteNumber("address", l.Address);
                writer.WriteNumber("file", l.File);
                writer.WriteNumber("line", l.Line);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("symbols");
            writer.WriteStartArray();
            foreach (var s in Symbols)
            {
                writer.WriteStartObject();
                writer.WriteString("name", s.Name);
                writer.WriteNumber("address", s.Address);
                writer.WriteString("kind", SymbolKindJson.ToJson(s.Kind));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("functions");
            writer.WriteStartArray();
            foreach (var f in Functions)
            {
                writer.WriteStartObject();
                writer.WriteString("name", f.Name);
                writer.WriteNumber("entry", f.Entry);
                writer.WriteNumber("end", f.End);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WritePropertyName("sections");
            writer.WriteStartArray();
            foreach (var s in Sections)
            {
                writer.WriteStartObject();
                writer.WriteNumber("origin", s.Origin);
                writer.WriteNumber("length", s.Length);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Parse a v1 JSON document produced by the assembler.</summary>
    public static St16dFile FromJson(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        int version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 1;

        var sources = new List<SourceFile>();
        if (root.TryGetProperty("sources", out var sourcesEl))
            foreach (var el in sourcesEl.EnumerateArray())
                sources.Add(new SourceFile(
                    el.GetProperty("name").GetString() ?? string.Empty,
                    el.GetProperty("text").GetString() ?? string.Empty));

        var lines = new List<LineInfo>();
        if (root.TryGetProperty("lines", out var linesEl))
            foreach (var el in linesEl.EnumerateArray())
                lines.Add(new LineInfo(
                    el.GetProperty("address").GetInt32(),
                    el.GetProperty("file").GetInt32(),
                    el.GetProperty("line").GetInt32()));

        var symbols = new List<SymbolInfo>();
        if (root.TryGetProperty("symbols", out var symbolsEl))
            foreach (var el in symbolsEl.EnumerateArray())
                symbols.Add(new SymbolInfo(
                    el.GetProperty("name").GetString() ?? string.Empty,
                    el.GetProperty("address").GetInt32(),
                    SymbolKindJson.FromJson(el.GetProperty("kind").GetString() ?? "label")));

        var functions = new List<FunctionInfo>();
        if (root.TryGetProperty("functions", out var functionsEl))
            foreach (var el in functionsEl.EnumerateArray())
                functions.Add(new FunctionInfo(
                    el.GetProperty("name").GetString() ?? string.Empty,
                    el.GetProperty("entry").GetInt32(),
                    el.GetProperty("end").GetInt32()));

        var sections = new List<SectionInfo>();
        if (root.TryGetProperty("sections", out var sectionsEl))
            foreach (var el in sectionsEl.EnumerateArray())
                sections.Add(new SectionInfo(
                    el.GetProperty("origin").GetInt32(),
                    el.GetProperty("length").GetInt32()));

        return new St16dFile
        {
            Version = version,
            Sources = sources,
            Lines = lines,
            Symbols = symbols,
            Functions = functions,
            Sections = sections,
        };
    }
}
