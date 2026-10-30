using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>Operand forms (spec §8.2): decimal, 0x hex, 0b binary, label, label ± const.</summary>
public class OperandFormTests
{
    [Fact]
    public void Decimal_Operand()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 42\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal((ushort)0x1B2A, r.Words[0]);
    }

    [Fact]
    public void NegativeDecimal_Operand()
    {
        var r = Assembler.Assemble("t.st16", "PUSH -7\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal((ushort)0x1BF9, r.Words[0]); // -7 & 0xFF
    }

    [Fact]
    public void Hex_Operand()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 0x2A\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal((ushort)0x1B2A, r.Words[0]);
    }

    [Fact]
    public void Hex_16BitValue_Push16()
    {
        var r = Assembler.Assemble("t.st16", "PUSH16 0x8000\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x1C00, 0x8000 }, r.Words);
    }

    [Fact]
    public void Binary_Operand()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 0b1010\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal((ushort)0x1B0A, r.Words[0]);
    }

    [Fact]
    public void Label_AsAbsoluteTarget()
    {
        var r = Assembler.Assemble("t.st16", "loop:\nJMP loop\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x1F00, 0x0000 }, r.Words);
    }

    [Fact]
    public void LabelPlusConst_ForPush16()
    {
        var r = Assembler.Assemble("t.st16", "loop:\nPUSH16 loop + 2\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x1C00, 0x0002 }, r.Words);
    }

    [Fact]
    public void LabelPlusConst_ForCall()
    {
        var r = Assembler.Assemble("t.st16", "a:\n.ds 5\nb:\nCALL b + 1\n");
        TestSource.AssertSucceeded(r);
        // a@0, .ds 5 → b@5, target 6.
        Assert.Equal(new ushort[] { 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x2000, 0x0006 }, r.Words);
    }

    [Fact]
    public void LabelMinusConst_ForJmp()
    {
        var r = Assembler.Assemble("t.st16", "a:\n.ds 10\nb:\nJMP b - 3\n");
        TestSource.AssertSucceeded(r);
        // a@0, .ds 10 → words 0..9, b@10, target 10 - 3 = 7.
        Assert.Equal(new ushort[] { 0x1F00, 0x0007 }, r.Words.Skip(10).ToArray());
    }

    [Fact]
    public void ConstPlusLabel_Form()
    {
        var r = Assembler.Assemble("t.st16", "a:\n.ds 5\nPUSH16 1 + a\n");
        TestSource.AssertSucceeded(r);
        // a@0 → 1 + 0 = 1.
        Assert.Equal(new ushort[] { 0x1C00, 0x0001 }, r.Words.Skip(5).ToArray());
    }

    [Fact]
    public void LabelPlusConst_NoWhitespace()
    {
        var r = Assembler.Assemble("t.st16", "a:\n.ds 5\nJMP a+4\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x1F00, 0x0004 }, r.Words.Skip(5).ToArray());
    }

    [Fact]
    public void Mnemonics_AreCaseInsensitive()
    {
        var r = Assembler.Assemble("t.st16", "dup\npush16 0x1\nload\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x0000, 0x1C00, 0x0001, 0x1D00 }, r.Words);
    }

    [Fact]
    public void Label_SharedLineWithInstruction()
    {
        var r = Assembler.Assemble("t.st16", "loop:PUSH 1\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x1B01 }, r.Words);

        var sym = r.Debug.Symbols.Single(s => s.Name == "loop" && s.Kind == "label");
        Assert.Equal(0, sym.Address);
    }

    [Fact]
    public void Whitespace_IsFlexible()
    {
        var r = Assembler.Assemble("t.st16", "  PUSH\t5  \n");
        TestSource.AssertSucceeded(r);
        Assert.Equal((ushort)0x1B05, r.Words[0]);
    }

    [Fact]
    public void Comment_IsStrippedToEndOfLine()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 5 ; five\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal((ushort)0x1B05, r.Words[0]);
    }

    [Fact]
    public void CommentOnlyAndBlankLines_AreIgnored()
    {
        var r = Assembler.Assemble("t.st16", "; c\n\n   \n\t\nPUSH 1\n");
        TestSource.AssertSucceeded(r);
        Assert.Single(r.Words);
    }
}
