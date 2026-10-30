using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Multi-file assembly: labels resolve across files, sources are embedded
/// per file, and Lines.File indexes the right source.
/// </summary>
public class MultiFileTests
{
    [Fact]
    public void Labels_SpanFiles_ForwardReferenceResolves()
    {
        var f1 = new SourceFile("a.st16", "PUSH 1\nCALL f\nHALT\n");
        var f2 = new SourceFile("b.st16", "f:\n    RET\n");

        var r = Assembler.Assemble(f1, f2);
        TestSource.AssertSucceeded(r);

        // f is at 4 (after PUSH/CALL header+value/HALT…: 1 + 2 + 1 = 4 words).
        Assert.Equal(new ushort[] { 0x1B01, 0x2000, 0x0004, 0x2400, 0x2100 }, r.Words);
    }

    [Fact]
    public void Sources_EmbedBothFiles_Verbatim()
    {
        var f1 = new SourceFile("a.st16", "PUSH 1\nCALL f\nHALT\n");
        var f2 = new SourceFile("b.st16", "f:\n    RET\n");
        var r = Assembler.Assemble(f1, f2);
        TestSource.AssertSucceeded(r);

        Assert.Equal(new[] { "a.st16", "b.st16" }, r.Debug.Sources.Select(s => s.Name).ToArray());
        Assert.Equal(f1.Text, r.Debug.Sources[0].Text);
        Assert.Equal(f2.Text, r.Debug.Sources[1].Text);
    }

    [Fact]
    public void Lines_FileIndex_MatchesSourceFile()
    {
        var f1 = new SourceFile("a.st16", "PUSH 1\nCALL f\nHALT\n");
        var f2 = new SourceFile("b.st16", "f:\n    RET\n");
        var r = Assembler.Assemble(f1, f2);
        TestSource.AssertSucceeded(r);

        // Word 0 = PUSH (a.st16 line 1); word 3 = HALT (a.st16 line 3);
        // word 4 = RET (b.st16 line 2).
        Assert.Equal((0, 1), (r.Debug.Lines[0].File, r.Debug.Lines[0].Line));
        Assert.Equal((0, 3), (r.Debug.Lines[3].File, r.Debug.Lines[3].Line));
        Assert.Equal((1, 2), (r.Debug.Lines[4].File, r.Debug.Lines[4].Line));
    }

    [Fact]
    public void DuplicateLabel_AcrossFiles_Errors()
    {
        var f1 = new SourceFile("a.st16", "a:\nPUSH 1\n");
        var f2 = new SourceFile("b.st16", "a:\nPUSH 2\n");

        var r = Assembler.Assemble(f1, f2);
        Assert.False(r.Success);
        Assert.Contains("duplicate label", TestSource.FirstError(r).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PositionCounter_ContinuesAcrossFiles()
    {
        var f1 = new SourceFile("a.st16", ".dw 1\n.dw 2\n");
        var f2 = new SourceFile("b.st16", "JMP 1\n");

        var r = Assembler.Assemble(f1, f2);
        TestSource.AssertSucceeded(r);

        // JMP in the second file encodes the absolute target 1, unaffected by files.
        Assert.Equal(new ushort[] { 0x0001, 0x0002, 0x1F00, 0x0001 }, r.Words);
        Assert.Equal(0, r.Debug.Origin);
        Assert.Equal(4, r.Debug.Lines.Count);
    }
}
