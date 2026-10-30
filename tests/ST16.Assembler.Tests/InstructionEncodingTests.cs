using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Every defined mnemonic (spec §5, 37 opcodes 0x00–0x24) encodes correctly:
/// word = (opcode &lt;&lt; 8) | operandField; two-word forms carry a value word.
/// </summary>
public class InstructionEncodingTests
{
    /// <summary>(source line, expected words) for each of the 37 mnemonics.</summary>
    public static TheoryData<string, ushort[]> Encoding
    {
        get
        {
            var data = new TheoryData<string, ushort[]>();

            // §5.1 stack manipulation
            data.Add("DUP", new ushort[] { 0x0000 });
            data.Add("SWAP", new ushort[] { 0x0100 });
            data.Add("OVER", new ushort[] { 0x0200 });
            data.Add("ROT", new ushort[] { 0x0300 });
            data.Add("DROP", new ushort[] { 0x0400 });
            // §5.2 arithmetic
            data.Add("ADD", new ushort[] { 0x0500 });
            data.Add("SUB", new ushort[] { 0x0600 });
            data.Add("NEG", new ushort[] { 0x0700 });
            data.Add("MUL", new ushort[] { 0x0800 });
            data.Add("DIV", new ushort[] { 0x0900 });
            data.Add("MOD", new ushort[] { 0x0A00 });
            data.Add("AND", new ushort[] { 0x0B00 });
            data.Add("OR", new ushort[] { 0x0C00 });
            data.Add("XOR", new ushort[] { 0x0D00 });
            data.Add("NOT", new ushort[] { 0x0E00 });
            data.Add("SLL", new ushort[] { 0x0F00 });
            data.Add("SLR", new ushort[] { 0x1000 });
            // §5.3 comparison
            data.Add("EQ", new ushort[] { 0x1100 });
            data.Add("NE", new ushort[] { 0x1200 });
            data.Add("LT", new ushort[] { 0x1300 });
            data.Add("GT", new ushort[] { 0x1400 });
            data.Add("LE", new ushort[] { 0x1500 });
            data.Add("GE", new ushort[] { 0x1600 });
            data.Add("ULT", new ushort[] { 0x1700 });
            data.Add("UGT", new ushort[] { 0x1800 });
            data.Add("ULE", new ushort[] { 0x1900 });
            data.Add("UGE", new ushort[] { 0x1A00 });
            // §5.4 memory
            data.Add("PUSH 0", new ushort[] { 0x1B00 });
            data.Add("PUSH16 0", new ushort[] { 0x1C00, 0x0000 });
            data.Add("LOAD", new ushort[] { 0x1D00 });
            data.Add("STORE", new ushort[] { 0x1E00 });
            // §5.5 control flow
            data.Add("JMP 0", new ushort[] { 0x1F00, 0x0000 });
            data.Add("CALL 0", new ushort[] { 0x2000, 0x0000 });
            data.Add("RET", new ushort[] { 0x2100 });
            // JZ/JNZ target 1 → next-instruction address is 1 → offset 0.
            data.Add("JZ 1", new ushort[] { 0x2200 });
            data.Add("JNZ 1", new ushort[] { 0x2300 });
            data.Add("HALT", new ushort[] { 0x2400 });

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Encoding))]
    public void EachMnemonic_EncodesExpectedWords(string source, ushort[] expected)
    {
        var r = Assembler.Assemble("theory.st16", source);
        TestSource.AssertSucceeded(r);
        Assert.Equal(expected, r.Words);
    }

    [Fact]
    public void Opcode_SitsInBits13To8_OperandFieldInLowByte()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 5\n");
        TestSource.AssertSucceeded(r);
        ushort w = r.Words[0];

        Assert.Equal(0x1B, (byte)(w >> 8));   // opcode
        Assert.Equal(5, w & 0xFF);            // operand field
        Assert.Equal(0, w & 0xC000);          // top two bits are 0
    }

    [Fact]
    public void Push_BoundaryValues_EncodeAsTwoComplementByte()
    {
        AssertPush(127, 0x1B7F);
        AssertPush(-128, 0x1B80);
        AssertPush(-1, 0x1BFF);
    }

    [Fact]
    public void BranchOffset_Negative_EncodesAsTwoComplementByte()
    {
        // JZ 0 at address 0 targets address 0 → offset = 0 - (0 + 1) = -1.
        var r = Assembler.Assemble("t.st16", "JZ 0\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal((ushort)0x22FF, r.Words[0]);
    }

    [Fact]
    public void TwoWord_HeaderAlwaysHasZeroOperandField()
    {
        var r = Assembler.Assemble("t.st16", "JMP 0x1234\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x1F00, 0x1234 }, r.Words);
    }

    private static void AssertPush(int value, ushort expected)
    {
        var r = Assembler.Assemble("t.st16", $"PUSH {value}\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new[] { expected }, r.Words);
    }
}
