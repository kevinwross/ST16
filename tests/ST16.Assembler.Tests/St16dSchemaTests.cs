using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// The .st16d v1 document: schema version, embedded sources, line mapping,
/// symbol kinds, functions, sections, and JSON round-trip fidelity.
/// </summary>
public class St16dSchemaTests
{
    private static St16dFile AssembleStarloop()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return result.DebugSymbols!;
    }

    [Fact]
    public void Starloop_DebugSymbols_HaveVersionOne()
        => Assert.Equal(1, AssembleStarloop().Version);

    [Fact]
    public void Starloop_Sources_EmbedExactSourceText()
    {
        var d = AssembleStarloop();

        var source = Assert.Single(d.Sources);
        Assert.Equal("starloop.st16", source.Name);
        Assert.Equal(Programs.Starloop, source.Text);
    }

    [Fact]
    public void Starloop_Lines_HaveOneEntryPerWrittenWord()
    {
        var d = AssembleStarloop();

        Assert.Equal(25, d.Lines.Count);
        Assert.Equal(25, d.Lines.Select(l => l.Address).Distinct().Count()); // one per word
        for (int a = 0; a <= 0x18; a++)
            Assert.Contains(d.Lines, l => l.Address == a);
    }

    [Fact]
    public void Starloop_TwoWordInstructions_MapBothWordsToHeaderLine()
    {
        var d = AssembleStarloop();

        // Word 1 AND word 2 of the `PUSH16 cnt` header (source line 4) map to line 4.
        Assert.Equal(4, d.Lines.Single(l => l.Address == 1).Line);
        Assert.Equal(4, d.Lines.Single(l => l.Address == 2).Line);
        // Likewise the JMP loop header (source line 19).
        Assert.Equal(19, d.Lines.Single(l => l.Address == 20).Line);
        Assert.Equal(19, d.Lines.Single(l => l.Address == 21).Line);
        // And the data word (source line 24).
        Assert.Equal(24, d.Lines.Single(l => l.Address == 0x18).Line);
    }

    [Fact]
    public void Starloop_SymbolKinds_AreLabelLabelLabelData()
    {
        var d = AssembleStarloop();

        Assert.Equal(4, d.Symbols.Count);
        Assert.Equal(SymbolKind.Label, d.Symbols.Single(s => s.Name == "main").Kind);
        Assert.Equal(0, d.Symbols.Single(s => s.Name == "main").Address);
        Assert.Equal(SymbolKind.Label, d.Symbols.Single(s => s.Name == "loop").Kind);
        Assert.Equal(6, d.Symbols.Single(s => s.Name == "loop").Address);
        Assert.Equal(SymbolKind.Label, d.Symbols.Single(s => s.Name == "done").Kind);
        Assert.Equal(22, d.Symbols.Single(s => s.Name == "done").Address);
        Assert.Equal(SymbolKind.Data, d.Symbols.Single(s => s.Name == "cnt").Kind);
        Assert.Equal(24, d.Symbols.Single(s => s.Name == "cnt").Address);
    }

    [Fact]
    public void Starloop_Functions_IsEmpty()
        => Assert.Empty(AssembleStarloop().Functions);

    [Fact]
    public void Starloop_Sections_SingleSectionFromZeroLength25()
    {
        var sections = AssembleStarloop().Sections;

        var section = Assert.Single(sections);
        Assert.Equal(0, section.Origin);
        Assert.Equal(25, section.Length);
    }

    [Fact]
    public void Sq_CallTargetedLabel_IsFunctionWithEntryThreeEndSix()
    {
        var result = St16Assembler.Assemble(Programs.Sq, "sq.st16");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        var d = result.DebugSymbols!;

        var symbol = Assert.Single(d.Symbols);
        Assert.Equal("sq", symbol.Name);
        Assert.Equal(3, symbol.Address);
        Assert.Equal(SymbolKind.Function, symbol.Kind);

        var function = Assert.Single(d.Functions);
        Assert.Equal("sq", function.Name);
        Assert.Equal(3, function.Entry);
        Assert.Equal(6, function.End); // exclusive: DUP, MUL, RET
    }

    [Fact]
    public void OrgGap_Sections_AreTwoSectionsWithCorrectOriginsAndLengths()
    {
        var result = St16Assembler.Assemble("PUSH 9\nHALT\n.org 0x0010\nHALT\n.dw 1\n");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var sections = result.DebugSymbols!.Sections;
        Assert.Equal(2, sections.Count);
        Assert.Equal(0, sections[0].Origin);
        Assert.Equal(2, sections[0].Length);
        Assert.Equal(0x0010, sections[1].Origin);
        Assert.Equal(2, sections[1].Length);
    }

    [Fact]
    public void Json_RoundTrip_ProducesFieldEqualDocument()
    {
        var d = AssembleStarloop();
        var back = St16dFile.FromJson(d.ToJson());

        Assert.Equal(d.Version, back.Version);

        Assert.Equal(d.Sources.Count, back.Sources.Count);
        for (int i = 0; i < d.Sources.Count; i++)
        {
            Assert.Equal(d.Sources[i].Name, back.Sources[i].Name);
            Assert.Equal(d.Sources[i].Text, back.Sources[i].Text);
        }

        Assert.Equal(d.Lines.Count, back.Lines.Count);
        for (int i = 0; i < d.Lines.Count; i++)
        {
            Assert.Equal(d.Lines[i].Address, back.Lines[i].Address);
            Assert.Equal(d.Lines[i].File, back.Lines[i].File);
            Assert.Equal(d.Lines[i].Line, back.Lines[i].Line);
        }

        Assert.Equal(d.Symbols.Count, back.Symbols.Count);
        for (int i = 0; i < d.Symbols.Count; i++)
        {
            Assert.Equal(d.Symbols[i].Name, back.Symbols[i].Name);
            Assert.Equal(d.Symbols[i].Address, back.Symbols[i].Address);
            Assert.Equal(d.Symbols[i].Kind, back.Symbols[i].Kind);
        }

        Assert.Equal(d.Functions.Count, back.Functions.Count);
        for (int i = 0; i < d.Functions.Count; i++)
        {
            Assert.Equal(d.Functions[i].Name, back.Functions[i].Name);
            Assert.Equal(d.Functions[i].Entry, back.Functions[i].Entry);
            Assert.Equal(d.Functions[i].End, back.Functions[i].End);
        }

        Assert.Equal(d.Sections.Count, back.Sections.Count);
        for (int i = 0; i < d.Sections.Count; i++)
        {
            Assert.Equal(d.Sections[i].Origin, back.Sections[i].Origin);
            Assert.Equal(d.Sections[i].Length, back.Sections[i].Length);
        }
    }
}
