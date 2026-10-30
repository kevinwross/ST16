using ST16.Assembler;
using ST16.VM;

namespace ST16.Disassembler;

/// <summary>
/// One line of a disassembly: the reassemblable instruction text plus an
/// annotation comment (offset, resolved labels, cross-reference hints).
/// The text is always valid assembly: reassembling it reproduces the same
/// words at the same addresses.
/// </summary>
public sealed class DisassembledLine
{
    public DisassembledLine(int address, string text, string? comment)
    {
        Address = address;
        Text = text;
        Comment = comment;
    }

    /// <summary>Word offset this line covers (its first word).</summary>
    public int Address { get; }

    /// <summary>Valid assembly text (one or more words; <c>.ds</c> for zero runs).</summary>
    public string Text { get; }

    /// <summary>Annotation: resolved symbol names, hints. Null when none.</summary>
    public string? Comment { get; }
}

/// <summary>The result of disassembling a word image.</summary>
public sealed class Disassembly
{
    internal Disassembly(
        int? orgLine,
        IReadOnlyList<DisassembledLine> lines,
        IReadOnlyDictionary<int, IReadOnlyList<int>> callReferences,
        IReadOnlyDictionary<int, IReadOnlyList<int>> branchReferences)
    {
        OrgLine = orgLine;
        Lines = lines;
        CallReferences = callReferences;
        BranchReferences = branchReferences;
    }

    /// <summary>First written word (as a <c>.org</c>), when the image starts above 0x0000.</summary>
    public int? OrgLine { get; }

    /// <summary>Disassembly lines in address order.</summary>
    public IReadOnlyList<DisassembledLine> Lines { get; }

    /// <summary>Cross-references: target address → call-site addresses (CALL sites).</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> CallReferences { get; }

    /// <summary>Cross-references: target address → branch-site addresses (JMP/JZ/JNZ sites).</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> BranchReferences { get; }

    /// <summary>
    /// Render as assembly text — directly reassemblable: the address/label
    /// column is a comment, so the text is valid <c>.st16</c> source.
    /// </summary>
    public string ToText()
    {
        var sb = new System.Text.StringBuilder();
        if (OrgLine is int org)
            sb.Append(".org ").Append("0x" + org.ToString("X4")).Append('\n');
        foreach (var l in Lines)
        {
            var text = l.Text.PadRight(20);
            var comment = $"; 0x{l.Address:X4}{(l.Comment is null ? "" : " " + l.Comment)}";
            sb.Append(text).Append(comment).Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>
/// The ST16 disassembler: decodes a word image into annotated assembly.
/// With a <c>.st16d</c> file, branch targets and PUSH16 values resolve to
/// symbol names and call/branch cross-references are collected.
/// </summary>
public static class St16Disassembler
{
    /// <summary>
    /// Disassemble <paramref name="words"/> (a flat image from 0x0000).
    /// Leading zero words before the first written word are skipped (emitted
    /// as a single <c>.org</c>); with <paramref name="symbols"/>, the first
    /// section origin is used instead so programs starting at 0x0000 with a
    /// zero word (e.g. <c>DUP</c>) stay in place.
    /// </summary>
    public static Disassembly Disassemble(ReadOnlySpan<ushort> words, St16dFile? symbols = null)
    {
        var calls = new Dictionary<int, List<int>>();
        var branches = new Dictionary<int, List<int>>();
        var lines = new List<DisassembledLine>();

        if (words.Length == 0)
            return new Disassembly(null, lines, ToFrozen(calls), ToFrozen(branches));

        // address → symbol name (exact address match).
        var symbolAt = new Dictionary<int, string>();
        var lineAt = new Dictionary<int, (int File, int Line)>();
        if (symbols is not null)
        {
            foreach (var s in symbols.Symbols)
                symbolAt[s.Address] = s.Name;
            foreach (var l in symbols.Lines)
                lineAt[l.Address] = (l.File, l.Line);
        }

        int start = FindStart(words, symbols);

        for (int i = start; i < words.Length; )
        {
            ushort w = words[i];

            if (w == 0)
            {
                // Zero run: data (or a DUP when the .st16d says this word is code).
                int n = 1;
                while (i + n < words.Length && words[i + n] == 0)
                    n++;

                if (IsCodeZero(i, symbols, lineAt))
                {
                    lines.Add(new DisassembledLine(i, "DUP", SymbolComment(i, symbolAt)));
                    i++;
                    continue;
                }

                var comment = SymbolComment(i, symbolAt);
                lines.Add(new DisassembledLine(i, n == 1 ? ".ds 1" : $".ds {n}", comment));
                i += n;
                continue;
            }

            byte opcode = (byte)(w >> 8);
            if (!OpcodeTable.IsDefined(opcode))
            {
                // Reserved opcode or bits 15–14 set: emit verbatim (reassembles identically).
                lines.Add(new DisassembledLine(i, $".dw 0x{w:X4}", SymbolComment(i, symbolAt)));
                i++;
                continue;
            }

            var info = OpcodeTable.Get((Opcode)opcode);

            if (info.TwoWord)
            {
                if (i + 1 >= words.Length)
                {
                    // Dangling two-word header at the end of the image: emit verbatim.
                    lines.Add(new DisassembledLine(i, $".dw 0x{w:X4}", null));
                    i++;
                    continue;
                }

                int target = words[i + 1];
                var (text, comment) = BranchText(info.Mnemonic, i, target, symbolAt);
                if (info.Mnemonic == "CALL")
                    AddRef(calls, target, i);
                else
                    AddRef(branches, target, i);
                lines.Add(new DisassembledLine(i, text, comment));
                i += 2;
                continue;
            }

            switch (info.Operand)
            {
                case OperandKind.Signed8Immediate:
                {
                    int value = (int)(sbyte)(w & 0xFF);
                    lines.Add(new DisassembledLine(i, $"{info.Mnemonic} {value}", SymbolComment(i, symbolAt)));
                    i++;
                    break;
                }
                case OperandKind.Signed8Relative:
                {
                    int offset = (int)(sbyte)(w & 0xFF);
                    int target = (i + 1 + offset) & 0xFFFF;
                    var (text, comment) = BranchText(info.Mnemonic, i, target, symbolAt);
                    AddRef(branches, target, i);
                    lines.Add(new DisassembledLine(i, text, comment));
                    i++;
                    break;
                }
                default: // Ignored
                {
                    // Preserve a non-zero reserved operand byte verbatim so the
                    // round trip stays byte-identical for hand-built images.
                    if ((w & 0xFF) == 0)
                        lines.Add(new DisassembledLine(i, info.Mnemonic, SymbolComment(i, symbolAt)));
                    else
                        lines.Add(new DisassembledLine(i, $".dw 0x{w:X4}", SymbolComment(i, symbolAt)));
                    i++;
                    break;
                }
            }
        }

        return new Disassembly(
            start > 0 ? start : null,
            lines,
            ToFrozen(calls),
            ToFrozen(branches));
    }

    private static int FindStart(ReadOnlySpan<ushort> words, St16dFile? symbols)
    {
        if (symbols is not null && symbols.Sections.Count > 0)
            return symbols.Sections[0].Origin;

        int i = 0;
        while (i < words.Length && words[i] == 0)
            i++;
        return i;
    }

    /// <summary>True when a zero word should be decoded as <c>DUP</c> (the .st16d marks it as instruction code).</summary>
    private static bool IsCodeZero(int address, St16dFile? symbols, Dictionary<int, (int File, int Line)> lineAt)
    {
        if (symbols is null || !lineAt.TryGetValue(address, out var loc))
            return false;

        if (loc.File < 0 || loc.File >= symbols.Sources.Count)
            return false;
        var text = symbols.Sources[loc.File].Text.Replace("\r\n", "\n").Split('\n');
        if (loc.Line < 1 || loc.Line > text.Length)
            return false;

        var line = text[loc.Line - 1].Trim();
        int comment = line.IndexOf(';');
        if (comment >= 0)
            line = line[..comment].Trim();
        line = line.TrimEnd(':').Trim(); // strip a leading label
        return line.StartsWith("DUP", StringComparison.OrdinalIgnoreCase)
               && (line.Length == 3 || !char.IsLetterOrDigit(line[3]));
    }

    private static (string Text, string? Comment) BranchText(string mnemonic, int site, int target, Dictionary<int, string> symbolAt)
    {
        string? comment = symbolAt.TryGetValue(target, out var name) ? name : null;
        return ($"{mnemonic} 0x{target:X4}", comment);
    }

    private static string? SymbolComment(int address, Dictionary<int, string> symbolAt)
        => symbolAt.TryGetValue(address, out var name) ? name : null;

    private static void AddRef(Dictionary<int, List<int>> map, int target, int site)
    {
        if (!map.TryGetValue(target, out var list))
            map[target] = list = new List<int>();
        list.Add(site);
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<int>> ToFrozen(Dictionary<int, List<int>> map)
        => map.ToDictionary(k => k.Key, v => (IReadOnlyList<int>)v.Value);
}
