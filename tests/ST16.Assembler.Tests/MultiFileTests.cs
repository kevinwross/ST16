using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// Multi-file assembly: labels are global across files; diagnostics and
/// line mappings are per-file.
/// </summary>
public class MultiFileTests
{
    private static readonly SourceFile FileA = new("a.st16", "A:\n    .dw 5\n");
    private static readonly SourceFile FileB = new("b.st16", "    PUSH 9\n    PUSH16 A\n    HALT\n");

    [Fact]
    public void AssembleMany_LabelDefinedInFileA_UsableFromFileB()
    {
        var result = St16Assembler.AssembleMany([FileA, FileB]);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        // A is defined at 0x0000, so PUSH16 A emits the value word 0x0000.
        Assert.Equal(new ushort[] { 0x0005, 0x1B09, 0x1C00, 0x0000, 0x2400 }, result.Words.ToArray());
        var symbol = Assert.Single(result.DebugSymbols!.Symbols);
        Assert.Equal("A", symbol.Name);
        Assert.Equal(0, symbol.Address);
    }

    [Fact]
    public void AssembleMany_LineInfo_FileIndexesPointToRightSource()
    {
        var result = St16Assembler.AssembleMany([FileA, FileB]);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var lines = result.DebugSymbols!.Lines;

        Assert.Equal(5, lines.Count);
        Assert.Equal(0, lines[0].Address);
        Assert.Equal(0, lines[0].File);
        Assert.Equal(2, lines[0].Line); // ".dw 5" is line 2 of a.st16
        Assert.Equal(1, lines[1].File);
        Assert.Equal(1, lines[1].Line); // "PUSH 9" is line 1 of b.st16
        Assert.Equal(1, lines[2].File);
        Assert.Equal(2, lines[2].Line); // "PUSH16 A" header (both words)
        Assert.Equal(2, lines[3].Line);
        Assert.Equal(1, lines[4].File);
        Assert.Equal(3, lines[4].Line); // "HALT" is line 3 of b.st16
    }

    [Fact]
    public void AssembleMany_UndefinedLabel_ReportedAtPerFileLine()
    {
        var result = St16Assembler.AssembleMany(
            [new SourceFile("a.st16", "A:\nHALT\n"), new SourceFile("b.st16", "JZ nowhere\nHALT\n")]);

        Assert.False(result.Success);
        var d = Assert.Single(result.Diagnostics, x => x.Severity == Severity.Error);
        Assert.Contains("undefined label 'nowhere'", d.Message);
        Assert.Equal(1, d.Line); // line 1 of the file that uses the label
    }

    [Fact]
    public void AssembleMany_DuplicateLabelAcrossFiles_IsError()
    {
        var result = St16Assembler.AssembleMany(
            [new SourceFile("a.st16", "A:\nHALT\n"), new SourceFile("b.st16", "A:\nHALT\n")]);

        Assert.False(result.Success);
        var d = Assert.Single(result.Diagnostics, x => x.Severity == Severity.Error);
        Assert.Contains("duplicate label 'A'", d.Message);
        Assert.Equal(1, d.Line); // the second definition (line 1 of b.st16)
    }

    [Fact]
    public void AssembleMany_EmptyCollection_Throws()
        => Assert.Throws<ArgumentException>(() => St16Assembler.AssembleMany(Array.Empty<SourceFile>()));

    [Fact]
    public void AssembleMany_Sources_EmbeddedInOrder()
    {
        var result = St16Assembler.AssembleMany([FileA, FileB]);

        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var sources = result.DebugSymbols!.Sources;
        Assert.Equal(2, sources.Count);
        Assert.Equal("a.st16", sources[0].Name);
        Assert.Equal(FileA.Text, sources[0].Text);
        Assert.Equal("b.st16", sources[1].Name);
        Assert.Equal(FileB.Text, sources[1].Text);
    }
}
