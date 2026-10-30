using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// The spec §9 worked examples: both goldens assemble to the documented
/// words exactly.
/// </summary>
public class GoldenExamplesTests
{
    [Fact]
    public void Starloop_AssemblesToDocumentedTwentyFiveWords()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(25, result.Words.Count);
        Assert.Equal(Programs.StarloopWords, result.Words.ToArray());
        Assert.NotNull(result.DebugSymbols);
    }

    [Fact]
    public void Starloop_ToBinary_IsLittleEndianWordImage()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        byte[] bytes = result.ToBinary();
        Assert.Equal(50, bytes.Length); // 25 words × 2 bytes
        // Word 0x1B09 → bytes 09 1B; word 0x1C00 → 00 1C; word 0x0018 → 18 00.
        Assert.Equal(new byte[] { 0x09, 0x1B, 0x00, 0x1C, 0x18, 0x00, 0x00, 0x1E }, bytes[..8]);
    }

    [Fact]
    public void Starloop_BranchAndJumpWords_MatchSpecOffsets()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        // JZ done at 0x000E: offset +7 (done at 0x0016, next instruction 0x000F).
        Assert.Equal(0x2207, (int)result.Words[0x0E]);
        // JMP loop at 0x0014: two-word absolute branch to 0x0006.
        Assert.Equal(0x1F00, (int)result.Words[0x14]);
        Assert.Equal(0x0006, (int)result.Words[0x15]);
        // Data word at 0x0018 (.dw 0).
        Assert.Equal(0x0000, (int)result.Words[0x18]);
    }

    [Fact]
    public void Sq_AssemblesToDocumentedSixWords()
    {
        var result = St16Assembler.Assemble(Programs.Sq, "sq.st16");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(6, result.Words.Count);
        Assert.Equal(Programs.SqWords, result.Words.ToArray());
        Assert.NotNull(result.DebugSymbols);
    }

    [Fact]
    public void Sq_CallEncodesTargetThree()
    {
        var result = St16Assembler.Assemble(Programs.Sq, "sq.st16");

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        Assert.Equal(0x2000, (int)result.Words[1]);
        Assert.Equal(0x0003, (int)result.Words[2]);
    }
}
