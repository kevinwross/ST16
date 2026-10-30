using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Assembler error cases (spec §8.4): each must yield Success=false, empty words,
/// a line-numbered diagnostic of the right class, and all errors collected.
/// </summary>
public class ErrorTests
{
    [Fact]
    public void UndefinedLabel_Errors_WithLine()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 1\nJMP nowhere\n");
        Assert.False(r.Success);
        var d = TestSource.FirstError(r);
        Assert.Equal(2, d.Line);
        Assert.Contains("undefined label", d.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nowhere", d.Message);
    }

    [Fact]
    public void DuplicateLabel_Errors_WithLine()
    {
        var r = Assembler.Assemble("t.st16", "a:\nPUSH 1\na:\nPUSH 2\n");
        Assert.False(r.Success);
        var d = TestSource.FirstError(r);
        Assert.Equal(3, d.Line);
        Assert.Contains("duplicate label", d.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Push_512_OutOfRange()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 512\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Push_M513_OutOfRange()
    {
        var r = Assembler.Assemble("t.st16", "PUSH -513\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Push_128_OutOfRange_8BitField()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 128\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Push_M129_OutOfRange_8BitField()
    {
        var r = Assembler.Assemble("t.st16", "PUSH -129\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BranchOffset_TooFar_Forward()
    {
        // JZ@0; .ds 128 → words 1..128; far@129 → offset 128 > 127.
        var r = Assembler.Assemble("t.st16", "JZ far\n.ds 128\nfar:\nPUSH 0\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BranchOffset_TooFar_Backward()
    {
        // top@0; .ds 128 → words 1..128; JZ@128 → offset = 0 - 129 = -129 < -128.
        var r = Assembler.Assemble("t.st16", "top:\n.ds 128\nJZ top\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BranchOffset_Boundaries_Accepted()
    {
        // offset +127: JZ@0, target 128 → .ds 127 (words 1..127), target label @128.
        var forward = Assembler.Assemble("t.st16", "JZ far\n.ds 127\nfar:\nPUSH 0\n");
        TestSource.AssertSucceeded(forward);

        // offset -128: top@0, .ds 127 (words 1..127), JZ@127 → offset = 0 - 128.
        var backward = Assembler.Assemble("t.st16", "top:\n.ds 127\nJZ top\n");
        TestSource.AssertSucceeded(backward);
        Assert.Equal((ushort)0x2280, backward.Words[^1]); // -128 & 0xFF
    }

    [Fact]
    public void Org_Backwards_OverEmittedContent_Errors()
    {
        // .ds 4 → words 0..3; .org 2 points back into already-emitted content.
        var r = Assembler.Assemble("t.st16", ".ds 4\n.org 2\n");
        Assert.False(r.Success);
        Assert.Contains(".org", TestSource.FirstError(r).Message);
    }

    [Fact]
    public void Org_BeyondRam_Errors()
    {
        var r = Assembler.Assemble("t.st16", ".org 0xF000\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Emit_BeyondRam_Errors()
    {
        var r = Assembler.Assemble("t.st16", ".ds 61440\n");
        Assert.False(r.Success);
        Assert.Contains("RAM", TestSource.FirstError(r).Message);
    }

    [Fact]
    public void UnknownMnemonic_Errors_WithLine()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 1\nFROB\n");
        Assert.False(r.Success);
        var d = TestSource.FirstError(r);
        Assert.Equal(2, d.Line);
        Assert.Contains("unknown mnemonic", d.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Operand_OnInstructionTakingNone_Errors()
    {
        var r = Assembler.Assemble("t.st16", "ADD 5\n");
        Assert.False(r.Success);
        var d = TestSource.FirstError(r);
        Assert.Equal(1, d.Line);
        Assert.Contains("ADD", d.Message);
    }

    [Fact]
    public void MissingOperand_Errors()
    {
        var r = Assembler.Assemble("t.st16", "PUSH\n");
        Assert.False(r.Success);
        Assert.Contains("requires an operand", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dw_NegativeValue_Errors()
    {
        var r = Assembler.Assemble("t.st16", ".dw -1\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Db_ByteTooLarge_Errors()
    {
        var r = Assembler.Assemble("t.st16", ".db 0x100\n");
        Assert.False(r.Success);
        Assert.Contains("out of range", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Db_TooManyBytes_Errors()
    {
        var r = Assembler.Assemble("t.st16", ".db 1, 2, 3\n");
        Assert.False(r.Success);
    }

    [Fact]
    public void UnknownDirective_Errors()
    {
        var r = Assembler.Assemble("t.st16", ".foo 1\n");
        Assert.False(r.Success);
        Assert.Contains("unknown directive", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllErrors_AreCollected_NotJustFirst()
    {
        var r = Assembler.Assemble("t.st16", "FROB\nJMP nowhere\nPUSH 512\n");
        Assert.False(r.Success);
        var errors = r.Diagnostics.Where(d => d.Level == "error").ToList();
        Assert.True(errors.Count >= 3, $"expected >= 3 errors, got {errors.Count}");
    }

    [Fact]
    public void FailedResult_HasEmptyWords()
    {
        var r = Assembler.Assemble("t.st16", "FROB\n");
        Assert.False(r.Success);
        Assert.Empty(r.Words);
        Assert.Null(r.Debug);
    }
}
