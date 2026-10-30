using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>Directives (spec §8.3): .org, .dw, .db, .ds.</summary>
public class DirectiveTests
{
    [Fact]
    public void Org_SetsOrigin_AndSectionStart()
    {
        var r = Assembler.Assemble("t.st16", ".org 100\nPUSH 1\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x1B01 }, r.Words);

        Assert.Equal(100, r.Debug.Origin);
        Assert.Single(r.Debug.Sections);
        Assert.Equal(100, r.Debug.Sections[0].Address);
        Assert.Equal(1, r.Debug.Sections[0].Length);
        Assert.Equal(100, r.Debug.Lines[0].Address);
    }

    [Fact]
    public void Org_Gap_CreatesNewSection()
    {
        var r = Assembler.Assemble("t.st16", ".dw 1\n.org 50\n.dw 2\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x0001, 0x0002 }, r.Words);

        Assert.Equal(
            new (int, int)[] { (0, 1), (50, 1) },
            r.Debug.Sections.Select(s => (s.Address, s.Length)).ToArray());
    }

    [Fact]
    public void Dw_MultipleValues_IncludingHexAndBinary()
    {
        var r = Assembler.Assemble("t.st16", ".dw 1, 0x2A, 0b11\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x0001, 0x002A, 0x0003 }, r.Words);
    }

    [Fact]
    public void Dw_LabelValue()
    {
        var r = Assembler.Assemble("t.st16", "x:\n.ds 5\n.dw x\n");
        TestSource.AssertSucceeded(r);
        // x@0 (labels mark the current position), so .dw emits 0.
        Assert.Equal(new ushort[] { 0x0000, 0x0000, 0x0000, 0x0000, 0x0000, 0x0000 }, r.Words);
    }

    [Fact]
    public void Db_TwoBytes_FirstIsLowByte()
    {
        var r = Assembler.Assemble("t.st16", ".db 0x12, 0x34\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x3412 }, r.Words);
    }

    [Fact]
    public void Db_SingleByte_HighByteZero()
    {
        var r = Assembler.Assemble("t.st16", ".db 0x12\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x0012 }, r.Words);
    }

    [Fact]
    public void Ds_ZeroFills_AndEveryWordMapsToTheDsLine()
    {
        var r = Assembler.Assemble("t.st16", "PUSH 1\n.ds 4\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(new ushort[] { 0x1B01, 0, 0, 0, 0 }, r.Words);

        var dsLines = r.Debug.Lines.Skip(1).ToArray();
        Assert.All(dsLines, l => Assert.Equal(2, l.Line));
    }

    [Fact]
    public void MultipleOrg_ContinuesAfterForwardJump()
    {
        var r = Assembler.Assemble("t.st16", ".org 10\n.dw 1\n.org 20\n.dw 2\n");
        TestSource.AssertSucceeded(r);
        Assert.Equal(10, r.Debug.Origin);
        Assert.Equal(
            new (int, int)[] { (10, 1), (20, 1) },
            r.Debug.Sections.Select(s => (s.Address, s.Length)).ToArray());
    }
}
