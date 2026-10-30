using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Every assembler error class (spec §8.4): Success = false with the exact
/// Line/Column and a message substring; on error there is no output
/// (empty Words, null DebugSymbols).
/// </summary>
public class AssemblerErrorTests
{
    /// <summary>Assembles <paramref name="source"/>, asserts failure with no output, and returns the single error diagnostic.</summary>
    private static Diagnostic AssertError(string source, string messageSubstring, int line, int column)
    {
        var result = St16Assembler.Assemble(source);

        Assert.False(result.Success);
        Assert.Empty(result.Words);
        Assert.Null(result.DebugSymbols);

        var match = Assert.Single(result.Diagnostics, d => d.Severity == Severity.Error && d.Message.Contains(messageSubstring));
        Assert.Equal(line, match.Line);
        Assert.Equal(column, match.Column);
        return match;
    }

    [Fact]
    public void UndefinedLabel_IsError()
        => AssertError("JZ nowhere\nHALT\n", "undefined label 'nowhere'", 1, 1);

    [Fact]
    public void UndefinedLabel_InDw_IsError()
        => AssertError(".dw nowhere\n", "undefined label 'nowhere'", 1, 1);

    [Fact]
    public void DuplicateLabel_IsError()
        => AssertError("foo:\nHALT\nfoo:\n", "duplicate label 'foo'", 3, 1);

    [Fact]
    public void Push_OperandOutOfRange_IsError()
    {
        var d = AssertError("PUSH 128", "out of range", 1, 1);
        Assert.Contains("PUSH operand 128", d.Message);
    }

    [Fact]
    public void Push_NegativeOutOfRange_IsError()
    {
        var d = AssertError("PUSH -129", "out of range", 1, 1);
        Assert.Contains("PUSH operand -129", d.Message);
    }

    [Fact]
    public void Jz_TargetOutOfRange_IsError()
        => AssertError("JZ 0x10000", "JZ target 65536 is out of range", 1, 1);

    [Fact]
    public void Push16_TargetOutOfRange_IsError()
        => AssertError("PUSH16 0x10000", "PUSH16 target 65536 is out of range", 1, 1);

    [Fact]
    public void Jmp_TargetOutOfRange_IsError()
        => AssertError("JMP 0x10000", "JMP target 65536 is out of range", 1, 1);

    [Fact]
    public void Call_TargetOutOfRange_IsError()
        => AssertError("CALL 0x10000", "CALL target 65536 is out of range", 1, 1);

    [Fact]
    public void BranchOffset_ForwardOutOfRange_IsError()
        => AssertError("JZ far\n.ds 131\nfar:\nHALT\n", "JZ offset 131 is out of range", 1, 1);

    [Fact]
    public void BranchOffset_BackwardOutOfRange_IsError()
        => AssertError(".ds 132\nJZ 0\nHALT\n", "JZ offset -133 is out of range", 2, 1);

    [Fact]
    public void Org_Backwards_IsError()
        => AssertError(".org 0x0010\nHALT\n.org 0x0005\n", "moves the position counter backwards", 3, 1);

    [Fact]
    public void Overlap_SecondWriteOfSameWord_ReferencesEarlierLine()
    {
        // .org back onto already-written words: the backwards move is reported
        // (line 3) and the second write of 0x0005 reports the earlier line (line 4).
        var result = St16Assembler.Assemble(".org 0x0005\nHALT\n.org 0x0005\nHALT\n");

        Assert.False(result.Success);
        var overlap = Assert.Single(result.Diagnostics, d => d.Message.Contains("overlap at 0x0005"));
        Assert.Equal(4, overlap.Line);
        Assert.Equal(1, overlap.Column);
        Assert.Contains("line 2", overlap.Message);
    }

    [Fact]
    public void Org_TargetBeyondRam_IsError()
        => AssertError(".org 0xF000\nHALT\n", "beyond the RAM region", 1, 1);

    [Fact]
    public void Writing_Reaching0xF000_IsError()
        => AssertError(".org 0xEFFF\nPUSH16 1\n", "assembly reaches 0xF001", 2, 1);

    [Fact]
    public void UnknownMnemonic_IsError()
        => AssertError("FROB\n", "not a known mnemonic", 1, 1);

    [Fact]
    public void UnknownMnemonic_ReportsFirstNonWhitespaceColumn()
        => AssertError("    FROB\n", "not a known mnemonic", 1, 5);

    [Fact]
    public void OperandToNoOperandMnemonic_IsError()
        => AssertError("HALT 5\n", "HALT does not take an operand", 1, 1);

    [Fact]
    public void MalformedExpression_IsError()
        => AssertError("PUSH 0x\n", "not a valid expression", 1, 1);

    [Fact]
    public void MissingOperand_Directive_IsError()
        => AssertError(".org\n", "missing operand", 1, 1);

    [Fact]
    public void Dw_MissingArgument_IsError()
        => AssertError(".dw\n", "takes at least one expression", 1, 1);

    [Fact]
    public void Dw_InvalidArgument_IsError()
        => AssertError(".dw 0x\n", "not a valid expression", 1, 1);

    [Fact]
    public void Ds_InvalidArgument_IsError()
        => AssertError(".ds 1 2\n", "not a valid expression", 1, 1);

    [Fact]
    public void Db_TooManyBytes_IsError()
        => AssertError(".db 1, 2, 3\n", "one or two byte values", 1, 1);

    [Fact]
    public void Db_ByteOutOfRange_IsError()
        => AssertError(".db 0x100\n", ".db byte 256 is out of range", 1, 1);

    [Fact]
    public void Dw_ValueOutOfRange_IsError()
        => AssertError(".dw 0x10000\n", ".dw value 65536 is out of range", 1, 1);

    [Fact]
    public void UnknownDirective_IsError()
        => AssertError(".fr 1\n", "not a known directive", 1, 1);

    [Fact]
    public void ValidProgram_SucceedsWithZeroDiagnostics()
    {
        var result = St16Assembler.Assemble("main:\n    PUSH 9\n    HALT\n");

        Assert.True(result.Success);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Words.Count); // the label line writes no word
        Assert.NotNull(result.DebugSymbols);
    }
}
