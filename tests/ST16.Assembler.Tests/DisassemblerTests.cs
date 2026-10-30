using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Disassembler: formatted output, symbol/branch-target annotations, UNKNOWN
/// opcodes, and assemble → disassemble → assemble round-trip identity.
/// </summary>
public class DisassemblerTests
{
    [Fact]
    public void Starloop_AddressesAndMnemonics()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);
        var dis = Disassembler.Disassemble(TestSource.StarloopWords, 0, r.Debug);

        var lines = dis.Assembly.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // 18 instructions + the .dw data word at 0x0018.
        Assert.Equal(19, lines.Length);

        static string At(string[] ls, string addr) =>
            ls.Single(l => l.StartsWith($"0x{addr}:  ", StringComparison.Ordinal));

        Assert.Matches(@"PUSH\s+9", At(lines, "0000"));   // counter init per erratum
        Assert.Contains("; main", At(lines, "0000"));

        Assert.Contains("PUSH16", At(lines, "0001"));
        Assert.Contains("STORE", At(lines, "0003"));
        Assert.Contains("DUP", At(lines, "0006"));
        Assert.Contains("LOAD", At(lines, "000C"));
        Assert.Contains("JZ", At(lines, "000E"));
        Assert.Contains("JMP", At(lines, "0014"));
        Assert.Contains("DROP", At(lines, "0016"));
        Assert.Contains("HALT", At(lines, "0017"));
        Assert.Contains("DUP", At(lines, "0018")); // the .dw 0 word decodes as DUP
    }

    [Fact]
    public void Starloop_BranchTargetAnnotations()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);
        var text = Disassembler.Disassemble(TestSource.StarloopWords, 0, r.Debug).Assembly;

        // JZ done: target 0x0016 with the label resolved from the .st16d.
        Assert.Contains("; → 0x0016 (done)", text);
        // JMP loop: absolute target 0x0006.
        Assert.Contains("; → 0x0006 (loop)", text);
        // Branch operand renders as signed decimal (offset +7).
        var jzLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(l => l.StartsWith("0x000E:  ", StringComparison.Ordinal));
        Assert.Contains("  7", jzLine);
    }

    [Fact]
    public void Starloop_CntLabel_MappedViaPush16CrossReference()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);
        var text = Disassembler.Disassemble(TestSource.StarloopWords, 0, r.Debug).Assembly;

        // PUSH16 pushes 0x0018 == cnt: cross-reference hint with the label name.
        Assert.Contains("; → 0x0018 (cnt)", text);
    }

    [Fact]
    public void UnknownOpcode_Renders()
    {
        var dis = Disassembler.Disassemble(new ushort[] { 0x2A1F });
        var line = dis.Assembly.Split('\n', StringSplitOptions.RemoveEmptyEntries).First();
        Assert.Contains("UNKNOWN", line);
        Assert.Contains("0x2A1F", line);
    }

    [Fact]
    public void Call_NotesReturnAddress()
    {
        // CALL 4 at 0x0000: header 0x2000 + value 0x0004; return address = PC + 2.
        var dis = Disassembler.Disassemble(new ushort[] { 0x2000, 0x0004, 0x2400 });
        var line = dis.Assembly.Split('\n', StringSplitOptions.RemoveEmptyEntries).First();

        Assert.Contains("CALL", line);
        Assert.Contains("0x0004", line);
        Assert.Contains("ret → 0x0002", line);
    }

    [Fact]
    public void Origin_ShiftsRenderedAddresses()
    {
        var dis = Disassembler.Disassemble(TestSource.StarloopWords, 0x100);
        var first = dis.Assembly.Split('\n', StringSplitOptions.RemoveEmptyEntries).First();
        Assert.StartsWith("0x0100:  ", first);
    }

    [Fact]
    public void TruncatedTwoWordInstruction_ReportsDiagnostic()
    {
        var dis = Disassembler.Disassemble(new ushort[] { 0x1C00 });
        Assert.NotEmpty(dis.Diagnostics);
        Assert.Contains("value word", dis.Diagnostics[0].Message);
    }

    [Fact]
    public void EmptyImage_YieldsEmptyAssembly()
    {
        var dis = Disassembler.Disassemble(Array.Empty<ushort>());
        Assert.Empty(dis.Diagnostics);
        Assert.Equal(string.Empty, dis.Assembly.Trim());
    }

    [Fact]
    public void RoundTrip_Starloop_AssembleDisassembleAssemble()
    {
        RoundTrip.AssertRoundTrips(TestSource.StarloopWords);
    }

    [Fact]
    public void RoundTrip_Sq_AssembleDisassembleAssemble()
    {
        RoundTrip.AssertRoundTrips(TestSource.SqWords);
    }
}

/// <summary>
/// Feeds the disassembler's mnemonic/operand tokens back through the assembler
/// (branch offsets converted to numeric target addresses) and requires the
/// reassembled words to be identical.
/// </summary>
public static class RoundTrip
{
    // Mnemonics may contain digits (PUSH16), hence [A-Z0-9]+.
    private static readonly Regex LinePattern =
        new(@"^0x(?<a>[0-9A-F]{4}):\s+(?<m>[A-Z0-9]+)(?:\s+(?<o>-?\d+|0x[0-9A-F]{4}))?\s*(?:;.*)?$",
            RegexOptions.Compiled);

    public static void AssertRoundTrips(ushort[] words)
    {
        var dis = Disassembler.Disassemble(words, 0, null);
        Assert.Empty(dis.Diagnostics);

        var sb = new StringBuilder();
        foreach (string line in dis.Assembly.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = LinePattern.Match(line);
            Assert.True(m.Success, $"unparseable disassembly line: {line}");

            string mnem = m.Groups["m"].Value;
            string? operand = m.Groups["o"].Success ? m.Groups["o"].Value : null;
            int addr = int.Parse(m.Groups["a"].Value, NumberStyles.HexNumber);

            if (mnem is "JZ" or "JNZ")
            {
                // The disassembler prints the relative offset; reassemble as an
                // absolute target (labels replaced by numeric addresses).
                int target = addr + 1 + int.Parse(operand!);
                sb.AppendLine($"{mnem} {target}");
            }
            else
            {
                sb.AppendLine(operand is null ? mnem : $"{mnem} {operand}");
            }
        }

        var r = Assembler.Assemble("roundtrip.st16", sb.ToString());
        TestSource.AssertSucceeded(r);
        Assert.Equal(words, r.Words);
    }
}
