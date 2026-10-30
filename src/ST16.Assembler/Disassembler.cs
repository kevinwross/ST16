#nullable enable
using System.Text;

namespace ST16.Assembler;

/// <summary>
/// Result of <see cref="Disassembler.Disassemble"/>: annotated assembly text
/// (one instruction per line) plus diagnostics (e.g. a truncated two-word
/// instruction at the end of the image).
/// </summary>
public sealed class DisassembleResult
{
    public string Assembly { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    internal DisassembleResult(string assembly, IReadOnlyList<Diagnostic> diagnostics)
    {
        Assembly = assembly;
        Diagnostics = diagnostics;
    }
}

/// <summary>
/// ST16 disassembler: renders a word image as annotated assembly, one
/// instruction per line:
///
/// <code>
/// 0x0000:  PUSH      10   ; main
/// 0x0001:  PUSH16    0x0018   ; → 0x0018 (cnt)
/// 0x000E:  JZ        7   ; → 0x0016 (done)
/// </code>
///
/// Operand rendering: PUSH/JZ/JNZ as signed decimal; PUSH16/JMP/CALL as
/// 0xXXXX hex; no-operand instructions render no operand. Branch targets
/// (JZ/JNZ/JMP/CALL) are computed and annotated as <c>; → 0xXXXX</c>, with
/// the symbol name appended when the .st16d maps the target. CALL also notes
/// its return address (PC + 2, spec §12.8). PUSH16 gets a cross-reference
/// hint when its literal matches a known label. Reserved opcodes 0x25–0x3F
/// render as <c>UNKNOWN  0xXXXX</c>. Two-word instructions consume their
/// value word; it is never printed as its own line.
/// </summary>
public static class Disassembler
{
    public static DisassembleResult Disassemble(ushort[] words, ushort origin = 0, St16dDebugFile? debug = null)
    {
        var diagnostics = new List<Diagnostic>();
        var sb = new StringBuilder();

        var labelAt = new Dictionary<int, string>();
        if (debug != null)
        {
            foreach (var s in debug.Symbols)
            {
                if (!string.IsNullOrEmpty(s.Name) && !labelAt.ContainsKey(s.Address))
                    labelAt[s.Address] = s.Name;
            }
        }

        int i = 0;
        while (i < words.Length)
        {
            int addr = origin + i;
            ushort w = words[i];
            var info = Opcodes.ByOpcode(Opcodes.OpcodeOf(w));

            string mnemonic = info != null ? info.Mnemonic : "UNKNOWN";
            string? operand = null;
            var notes = new List<string>();
            int size = 1;

            if (labelAt.TryGetValue(addr, out string? self))
                notes.Add(self); // symbol mapped at this instruction's address

            if (info is null)
            {
                // Reserved opcode 0x25–0x3F (ILLOP on the machine).
                operand = "0x" + w.ToString("X4");
            }
            else if (info.Operand == Opcodes.OperandKind.Absolute16)
            {
                if (i + 1 >= words.Length)
                {
                    diagnostics.Add(new Diagnostic(
                        "error",
                        $"{info.Mnemonic} at 0x{addr:X4} is missing its value word",
                        0, 0));
                    break;
                }

                int value = words[i + 1];
                operand = "0x" + value.ToString("X4");
                size = 2;

                if (info.Mnemonic is "JMP" or "CALL")
                {
                    AddTargetNote(value, notes, labelAt);
                    if (info.Mnemonic == "CALL")
                    {
                        int ret = (addr + 2) & 0xFFFF; // spec §12.8: return address is PC + 2
                        notes.Add($"ret → 0x{ret:X4}");
                    }
                }
                else // PUSH16: cross-reference hint when the literal is a label.
                {
                    if (labelAt.TryGetValue(value, out string? lit))
                        notes.Add($"→ 0x{value:X4} ({lit})");
                }
            }
            else if (info.Operand != Opcodes.OperandKind.None)
            {
                int signed = Opcodes.SignedOperand(w);
                operand = signed.ToString();

                if (info.Operand == Opcodes.OperandKind.RelativeOffset)
                {
                    int target = (addr + 1 + signed) & 0xFFFF; // spec §12.7: relative to PC + 1
                    AddTargetNote(target, notes, labelAt);
                }
            }

            var line = new StringBuilder();
            line.Append("0x").Append(addr.ToString("X4")).Append(":  ");
            line.Append(mnemonic.PadRight(8));
            if (operand != null)
                line.Append("  ").Append(operand);
            foreach (string note in notes)
                line.Append("   ; ").Append(note);
            sb.AppendLine(line.ToString());

            i += size;
        }

        return new DisassembleResult(sb.ToString(), diagnostics);
    }

    private static void AddTargetNote(int target, List<string> notes, IReadOnlyDictionary<int, string> labelAt)
    {
        string note = "→ 0x" + target.ToString("X4");
        if (labelAt.TryGetValue(target, out string? name))
            note += $" ({name})";
        notes.Add(note);
    }
}
