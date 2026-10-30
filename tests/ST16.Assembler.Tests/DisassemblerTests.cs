using ST16.Disassembler;
using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Disassembler decode rules: operand interpretation, verbatim .dw for
/// undecodable words, .ds for zero runs, dangling two-word headers, OrgLine,
/// symbol resolution, cross-references, and ToText.
/// </summary>
public class DisassemblerTests
{
    [Theory]
    [InlineData(0x1B07, "PUSH 7")]
    [InlineData(0x1BF9, "PUSH -7")]
    [InlineData(0x1B80, "PUSH -128")]
    [InlineData(0x1B7F, "PUSH 127")]
    public void Push_DecodesSignedDecimalImmediate(ushort word, string expected)
    {
        var d = St16Disassembler.Disassemble(new ushort[] { word });

        var line = Assert.Single(d.Lines);
        Assert.Equal(expected, line.Text);
        Assert.Equal(0, line.Address);
    }

    [Theory]
    [InlineData(0x2207, "JZ 0x0008")]  // offset +7 from PC+1
    [InlineData(0x23FF, "JNZ 0x0000")] // offset −1
    public void JzJnz_DecodeAbsoluteTargetHex(ushort word, string expected)
    {
        var d = St16Disassembler.Disassemble(new ushort[] { word });

        var line = Assert.Single(d.Lines);
        Assert.Equal(expected, line.Text);
    }

    [Fact]
    public void TwoWordInstructions_DecodeValueAndAdvanceTwo()
    {
        var d = St16Disassembler.Disassemble(new ushort[] { 0x1C00, 0x0018, 0x2400 });

        Assert.Equal(2, d.Lines.Count);
        Assert.Equal("PUSH16 0x0018", d.Lines[0].Text);
        Assert.Equal(0, d.Lines[0].Address);
        Assert.Equal("HALT", d.Lines[1].Text);
        Assert.Equal(2, d.Lines[1].Address);
    }

    [Theory]
    [InlineData(0x2500, ".dw 0x2500")] // reserved opcode
    [InlineData(0xC000, ".dw 0xC000")] // bits 15–14 set
    [InlineData(0x24FF, ".dw 0x24FF")] // HALT with a non-zero reserved operand byte
    public void UndecodableWord_EmittedVerbatimAsDw(ushort word, string expected)
    {
        var d = St16Disassembler.Disassemble(new ushort[] { word });

        var line = Assert.Single(d.Lines);
        Assert.Equal(expected, line.Text);
    }

    [Fact]
    public void ZeroRun_DecodesAsSingleDsLine()
    {
        var d = St16Disassembler.Disassemble(new ushort[] { 0x1B01, 0x0000, 0x0000, 0x0000 });

        Assert.Equal(2, d.Lines.Count);
        Assert.Equal(".ds 3", d.Lines[1].Text);
        Assert.Equal(1, d.Lines[1].Address);
    }

    [Fact]
    public void DanglingTwoWordHeader_EmittedAsDw()
    {
        var d = St16Disassembler.Disassemble(new ushort[] { 0x1B09, 0x1F00 });

        Assert.Equal(2, d.Lines.Count);
        Assert.Equal("PUSH 9", d.Lines[0].Text);
        Assert.Equal(".dw 0x1F00", d.Lines[1].Text);
        Assert.Equal(1, d.Lines[1].Address);
    }

    [Fact]
    public void OrgLine_SetForImageStartingAt0x0010_WithoutSymbols()
    {
        var words = new ushort[18];
        words[16] = 0x1B09;
        words[17] = 0x2400;

        var d = St16Disassembler.Disassemble(words);

        Assert.Equal(0x0010, d.OrgLine);
        Assert.Equal(2, d.Lines.Count);
        Assert.Equal(0x10, d.Lines[0].Address);
    }

    [Fact]
    public void OrgLine_SetForImageStartingAt0x0010_WithSymbols()
    {
        var result = St16Assembler.Assemble(".org 0x0010\nPUSH 9\nHALT\n");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var d = St16Disassembler.Disassemble(result.Words.ToArray(), result.DebugSymbols);

        Assert.Equal(0x0010, d.OrgLine);
        Assert.Equal(0x10, d.Lines[0].Address);
    }

    [Fact]
    public void ZeroWord_MappedToDupSourceLine_DecodesAsDup_WithSymbols()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var d = St16Disassembler.Disassemble(result.Words.ToArray(), result.DebugSymbols);

        var line = d.Lines.Single(l => l.Address == 6);
        Assert.Equal("DUP", line.Text);
    }

    [Fact]
    public void ZeroWord_WithoutSymbols_DecodesAsDs()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var d = St16Disassembler.Disassemble(result.Words.ToArray(), null);

        var line = d.Lines.Single(l => l.Address == 6);
        Assert.Equal(".ds 1", line.Text);
    }

    [Fact]
    public void BranchTargets_CommentIsSymbolName_WithSymbols()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var d = St16Disassembler.Disassemble(result.Words.ToArray(), result.DebugSymbols);

        Assert.Equal("done", d.Lines.Single(l => l.Address == 0x0E).Comment); // JZ done
        Assert.Equal("loop", d.Lines.Single(l => l.Address == 0x14).Comment); // JMP loop
        Assert.Equal("cnt", d.Lines.Single(l => l.Address == 1).Comment);     // PUSH16 cnt
    }

    [Fact]
    public void CallReferences_MapTargetToCallSites()
    {
        var d = St16Disassembler.Disassemble(Programs.SqWords);

        var entry = Assert.Single(d.CallReferences);
        Assert.Equal(3, entry.Key);
        Assert.Equal(new[] { 1 }, entry.Value);
        Assert.Empty(d.BranchReferences);
    }

    [Fact]
    public void BranchReferences_MapTargetToBranchSites()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var d = St16Disassembler.Disassemble(result.Words.ToArray(), null);

        Assert.Equal(2, d.BranchReferences.Count);
        Assert.Equal(new[] { 20 }, d.BranchReferences[6]);  // JMP loop
        Assert.Equal(new[] { 14 }, d.BranchReferences[22]); // JZ done
        Assert.Empty(d.CallReferences);
    }

    [Fact]
    public void ToText_EmitsAddressComments()
    {
        var d = St16Disassembler.Disassemble(new ushort[] { 0x1B09, 0x2400 });
        string text = d.ToText();

        Assert.Contains("PUSH 9", text);
        Assert.Contains("; 0x0000", text);
        Assert.Contains("; 0x0001", text);
    }
}
