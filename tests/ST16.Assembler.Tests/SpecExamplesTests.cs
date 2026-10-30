using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>Spec §9 examples assembled word-for-word (the §9.3 golden encoding).</summary>
public class SpecExamplesTests
{
    [Fact]
    public void Starloop_AssemblesToGolden25Words()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);
        Assert.Equal(TestSource.StarloopWords, r.Words);
    }

    [Fact]
    public void Starloop_FirstSixWords_MatchSpec93()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);
        Assert.Equal(
            new ushort[] { 0x1B09, 0x1C00, 0x0018, 0x1E00, 0x1C00, 0x002A },
            r.Words.Take(6).ToArray());
    }

    [Fact]
    public void Starloop_JzDone_EncodesOffsetPlus7()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        // JZ at 0x000E: next instruction 0x000F, done at 0x0016 → offset +7.
        Assert.Equal((ushort)0x2207, r.Words[14]);
    }

    [Fact]
    public void Starloop_JmpLoop_IsTwoWordAbsolute()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        // JMP loop at 0x0014: header (opcode 0x1F, operand field 0) + absolute value 0x0006.
        Assert.Equal((ushort)0x1F00, r.Words[20]);
        Assert.Equal((ushort)0x0006, r.Words[21]);
    }

    [Fact]
    public void Starloop_DataWord_LandsAt0x18()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        // cnt: .dw 0 lands at 0x0018, so every "PUSH16 cnt" pushes 0x0018.
        Assert.Equal((ushort)0x0018, r.Words[2]);
        Assert.Equal((ushort)0x0000, r.Words[^1]);
    }

    [Fact]
    public void Sq_AssemblesToGoldenWords()
    {
        var r = Assembler.Assemble("sq.st16", TestSource.Sq);
        TestSource.AssertSucceeded(r);
        Assert.Equal(TestSource.SqWords, r.Words);
    }

    [Fact]
    public void WriteBin_EmitsLittleEndianBytes()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 10\nHALT\n");
        TestSource.AssertSucceeded(r);

        // The test's own bin dir (writable in every environment; %TEMP% can be sandbox-blocked).
        string path = Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            r.WriteBin(path);
            Assert.Equal(new byte[] { 0x0A, 0x1B, 0x00, 0x24 }, File.ReadAllBytes(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
