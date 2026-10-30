using ST16.Disassembler;
using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Round-trip identity (the P3 definition of done):
/// assemble → disassemble → reassemble(ToText()) yields a byte-identical
/// binary, with both symbols = null and symbols = the assembler's .st16d.
/// </summary>
public class RoundTripTests
{
    public static TheoryData<string> Programs_ = new()
    {
        { Programs.Starloop },   // two-word instructions, JZ/JMP, .dw data
        { Programs.Sq },         // CALL
        { Programs.OrgGap },     // .org &gt; 0, .dw/.db/.ds, most opcodes
        { Programs.AllOneWordOpcodes }, // every one-word opcode
    };

    [Theory]
    [MemberData(nameof(Programs_))]
    public void Program_Assemble_Disassemble_Reassemble_IsByteIdentical_WithoutSymbols(string source)
        => RoundTrip(source, null);

    [Theory]
    [MemberData(nameof(Programs_))]
    public void Program_Assemble_Disassemble_Reassemble_IsByteIdentical_WithSymbols(string source)
    {
        var first = St16Assembler.Assemble(source);
        Assert.True(first.Success, string.Join("; ", first.Diagnostics));
        RoundTrip(source, first.DebugSymbols);
    }

    /// <summary>assemble → Disassemble(words, symbols) → reassemble(ToText()) → same bytes.</summary>
    private static void RoundTrip(string source, St16dFile? symbols)
    {
        var first = St16Assembler.Assemble(source);
        Assert.True(first.Success, string.Join("; ", first.Diagnostics));

        var disassembly = St16Disassembler.Disassemble(first.Words.ToArray(), symbols);

        var second = St16Assembler.Assemble(disassembly.ToText(), "roundtrip.st16");
        Assert.True(second.Success, string.Join("; ", second.Diagnostics));

        Assert.Equal(first.ToBinary(), second.ToBinary());
        Assert.Equal(first.Words.Count, second.Words.Count);
    }
}
