using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Directives and expressions (spec §8.2, §8.3): .org/.dw/.db/.ds,
/// label arithmetic, hex/binary/signed-decimal operands, case-insensitive
/// mnemonics and directives, comments, and label-only lines.
/// </summary>
public class DirectivesExpressionTests
{
    [Fact]
    public void Org_ForwardMovesPositionCounter_LeadingZerosIncluded()
    {
        var result = St16Assembler.Assemble(".org 0x0004\nHALT\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(5, result.Words.Count);
        Assert.Equal(0, (int)result.Words[0]);
        Assert.Equal(0, (int)result.Words[1]);
        Assert.Equal(0, (int)result.Words[2]);
        Assert.Equal(0, (int)result.Words[3]);
        Assert.Equal(0x2400, (int)result.Words[4]);
        Assert.Equal(10, result.ToBinary().Length); // (lastWritten + 1) × 2 bytes
    }

    [Fact]
    public void Dw_EmitsConstantsLabelsAndLabelPlusConstant()
    {
        var result = St16Assembler.Assemble("foo:\n    .dw foo + 1, 0x0005, 0b1010\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(new ushort[] { 0x0001, 0x0005, 0x000A }, result.Words.ToArray());
    }

    [Fact]
    public void Db_FirstByteIsLowEightBits()
    {
        var result = St16Assembler.Assemble(".db 0x12, 0x34\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(new ushort[] { 0x3412 }, result.Words.ToArray());
    }

    [Fact]
    public void Db_SingleByte_GoesInLowEightBits()
    {
        var result = St16Assembler.Assemble(".db 0x34\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(new ushort[] { 0x0034 }, result.Words.ToArray());
    }

    [Fact]
    public void Ds_ReservesZeroWords()
    {
        var result = St16Assembler.Assemble(".ds 3\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(new ushort[] { 0x0000, 0x0000, 0x0000 }, result.Words.ToArray());
    }

    [Fact]
    public void Label_OnOwnLine_DefinesSymbol()
    {
        var result = St16Assembler.Assemble("loop:\n    HALT\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var symbol = Assert.Single(result.DebugSymbols!.Symbols);
        Assert.Equal("loop", symbol.Name);
        Assert.Equal(0, symbol.Address);
        Assert.Single(result.Words);
    }

    [Fact]
    public void Label_SpacedColonForm_DefinesSymbol()
    {
        var result = St16Assembler.Assemble("loop : HALT\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal("loop", Assert.Single(result.DebugSymbols!.Symbols).Name);
        Assert.Equal(0x2400, (int)result.Words[0]);
    }

    [Fact]
    public void Operands_AcceptHexBinaryAndSignedDecimal()
    {
        var result = St16Assembler.Assemble("PUSH 0b101\nPUSH -10\nPUSH16 0xFFFF\nHALT\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(new ushort[] { 0x1B05, 0x1BF6, 0x1C00, 0xFFFF, 0x2400 }, result.Words.ToArray());
    }

    [Fact]
    public void LowercaseMnemonicsAndDirectives_Accepted()
    {
        var result = St16Assembler.Assemble(".ORG 4\npush 9\nhalt\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(6, result.Words.Count);
        Assert.Equal(0x1B09, (int)result.Words[4]);
        Assert.Equal(0x2400, (int)result.Words[5]);
    }

    [Fact]
    public void Comment_AfterOperand_Ignored()
    {
        var result = St16Assembler.Assemble("PUSH 9 ; nine\nHALT\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(new ushort[] { 0x1B09, 0x2400 }, result.Words.ToArray());
    }

    [Fact]
    public void LabelMinusConstant_AcceptsAsciiMinus()
    {
        var result = St16Assembler.Assemble("    .ds 8\ntop:\n    .dw top - 4\n    .dw top + 1\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(10, result.Words.Count);
        Assert.Equal(0x0004, (int)result.Words[8]);
        Assert.Equal(0x0009, (int)result.Words[9]);
    }

    [Fact]
    public void LabelMinusConstant_AcceptsUnicodeMinus()
    {
        // U+2212 MINUS SIGN instead of ASCII '-'.
        var result = St16Assembler.Assemble("    .ds 8\ntop:\n    .dw top \u2212 4\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(0x0004, (int)result.Words[8]);
    }

    [Fact]
    public void ToBinary_IsLittleEndian()
    {
        var result = St16Assembler.Assemble(".dw 0x1234\n");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(new byte[] { 0x34, 0x12 }, result.ToBinary());
    }
}
