using ST16.VM;
using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Encoding table, instruction by instruction (spec §9.3): each of the 37
/// mnemonics assembles to its documented header word.
/// </summary>
public class EncodingTableTests
{
    /// <summary>(source line as first instruction, expected header word, words written, second word or null).</summary>
    public static TheoryData<string, int, int, int?> EveryMnemonic = new()
    {
        // Stack manipulation (0x00–0x04).
        { "DUP", 0x0000, 1, null },
        { "SWAP", 0x0100, 1, null },
        { "OVER", 0x0200, 1, null },
        { "ROT", 0x0300, 1, null },
        { "DROP", 0x0400, 1, null },
        // Arithmetic (0x05–0x10).
        { "ADD", 0x0500, 1, null },
        { "SUB", 0x0600, 1, null },
        { "NEG", 0x0700, 1, null },
        { "MUL", 0x0800, 1, null },
        { "DIV", 0x0900, 1, null },
        { "MOD", 0x0A00, 1, null },
        { "AND", 0x0B00, 1, null },
        { "OR", 0x0C00, 1, null },
        { "XOR", 0x0D00, 1, null },
        { "NOT", 0x0E00, 1, null },
        { "SLL", 0x0F00, 1, null },
        { "SLR", 0x1000, 1, null },
        // Comparison (0x11–0x1A).
        { "EQ", 0x1100, 1, null },
        { "NE", 0x1200, 1, null },
        { "LT", 0x1300, 1, null },
        { "GT", 0x1400, 1, null },
        { "LE", 0x1500, 1, null },
        { "GE", 0x1600, 1, null },
        { "ULT", 0x1700, 1, null },
        { "UGT", 0x1800, 1, null },
        { "ULE", 0x1900, 1, null },
        { "UGE", 0x1A00, 1, null },
        // Memory (0x1B–0x1E).
        { "PUSH 9", 0x1B09, 1, null },
        { "PUSH16 0x0018", 0x1C00, 2, 0x0018 },
        { "LOAD", 0x1D00, 1, null },
        { "STORE", 0x1E00, 1, null },
        // Control flow (0x1F–0x24).
        { "JMP 0x0006", 0x1F00, 2, 0x0006 },
        { "CALL 0x0003", 0x2000, 2, 0x0003 },
        { "RET", 0x2100, 1, null },
        // JZ/JNZ encode target − (instr_addr + 1); as the first instruction 0x0007 → offset 6.
        { "JZ 0x0007", 0x2206, 1, null },
        { "JNZ 0x0007", 0x2306, 1, null },
        { "HALT", 0x2400, 1, null },
    };

    [Theory]
    [MemberData(nameof(EveryMnemonic))]
    public void Mnemonic_EncodesToDocumentedHeaderWord(string source, int header, int wordCount, int? secondWord)
    {
        var result = St16Assembler.Assemble(source);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(wordCount, result.Words.Count);
        Assert.Equal(header, (int)result.Words[0]);
        if (secondWord is int expected)
            Assert.Equal(expected, (int)result.Words[1]);
    }

    [Fact]
    public void EncodingTable_CoversAllThirtySevenMnemonics()
    {
        Assert.Equal(OpcodeTable.DefinedCount, EveryMnemonic.Count);
    }

    [Theory]
    [InlineData(0, 0x1B00)]
    [InlineData(127, 0x1B7F)]
    [InlineData(-128, 0x1B80)]
    [InlineData(-7, 0x1BF9)]
    public void Push_EncodesSigned8BitImmediate(int value, int word)
    {
        var result = St16Assembler.Assemble($"PUSH {value}");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(word, (int)result.Words[0]);
    }

    [Fact]
    public void Jz_EncodesOffsetFromNextInstruction()
    {
        // JZ at 0x0001 targeting 0x0000: offset = 0 − 2 = −2.
        var result = St16Assembler.Assemble("PUSH 1\nJZ 0x0000\n");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(0x22FE, (int)result.Words[1]);
    }

    [Fact]
    public void Jnz_EncodesOffsetFromNextInstruction()
    {
        // JNZ at 0x0002 targeting 0x0005: offset = 5 − 3 = 2.
        var result = St16Assembler.Assemble("PUSH 1\nHALT\nJNZ 0x0005\n");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(0x2302, (int)result.Words[2]);
    }

    [Fact]
    public void TwoWordInstructions_HeaderLowByteZeroAndValueFollows()
    {
        var result = St16Assembler.Assemble("PUSH16 0x1234\nJMP 0x0000\nCALL 0xFFFF\nHALT\n");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(7, result.Words.Count);
        Assert.Equal(0x1C00, (int)result.Words[0]);
        Assert.Equal(0x1234, (int)result.Words[1]);
        Assert.Equal(0x1F00, (int)result.Words[2]);
        Assert.Equal(0x0000, (int)result.Words[3]);
        Assert.Equal(0x2000, (int)result.Words[4]);
        Assert.Equal(0xFFFF, (int)result.Words[5]);
        Assert.Equal(0x2400, (int)result.Words[6]);
    }
}
