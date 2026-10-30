using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// .st16d content rules: schema fields, per-word line mapping (one entry per
/// emitted word), embedded sources, symbols, the function heuristic, JSON round-trip.
/// </summary>
public class St16dTests
{
    [Fact]
    public void Starloop_SchemaFields()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        var d = r.Debug;
        Assert.Equal("st16d", d.Format);
        Assert.Equal(1, d.Version);
        Assert.Equal(0, d.Origin);
        Assert.Single(d.Sources);
        Assert.Equal(25, d.Lines.Count);
        Assert.Single(d.Sections);
        Assert.Equal(0, d.Sections[0].Address);
        Assert.Equal(25, d.Sections[0].Length);
    }

    [Fact]
    public void EveryEmittedWord_HasExactlyOneLineEntry()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        Assert.Equal(25, r.Debug.Lines.Count);
        var byAddress = r.Debug.Lines.GroupBy(l => l.Address)
            .Select(g => (Address: g.Key, Count: g.Count())).ToArray();
        Assert.All(byAddress, t => Assert.Equal(1, t.Count));
        Assert.Equal(Enumerable.Range(0, 25), byAddress.Select(t => t.Address).OrderBy(a => a));
    }

    [Fact]
    public void TwoWordInstructions_MapBothWordsToSameLine()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        // Words 1..2 = "PUSH16 cnt" on source line 4.
        Assert.Equal(4, r.Debug.Lines[1].Line);
        Assert.Equal(4, r.Debug.Lines[2].Line);
        // Words 20..21 = "JMP loop" on source line 19.
        Assert.Equal(19, r.Debug.Lines[20].Line);
        Assert.Equal(19, r.Debug.Lines[21].Line);
    }

    [Fact]
    public void LineNumbers_MatchSourceLines()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        var expected = new[] { 3, 4, 4, 5, 6, 6, 8, 9, 9, 10, 11, 11, 12, 13, 14, 15, 16, 17, 17, 18, 19, 19, 21, 22, 24 };
        Assert.Equal(expected, r.Debug.Lines.Select(l => l.Line).ToArray());
    }

    [Fact]
    public void Sources_EmbedFullTextVerbatim()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        Assert.Equal("star.st16", r.Debug.Sources[0].Name);
        Assert.Equal(TestSource.Starloop, r.Debug.Sources[0].Text);
    }

    [Fact]
    public void Symbols_AllLabelsPresent()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        var labels = r.Debug.Symbols.Where(s => s.Kind == "label").ToList();
        Assert.Equal(
            new (string, int)[] { ("main", 0), ("loop", 6), ("done", 22), ("cnt", 24) },
            labels.Select(s => (s.Name, s.Address)).ToArray());
    }

    [Fact]
    public void Function_Heuristic_MainLoopDoneAreFunctions_CntIsNot()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        Assert.Equal(
            new (string, int, int)[] { ("main", 0, 6), ("loop", 6, 22), ("done", 22, 24) },
            r.Debug.Functions.Select(f => (f.Name, f.Entry, f.End)).ToArray());

        var functionSymbols = r.Debug.Symbols.Where(s => s.Kind == "function").ToList();
        Assert.Equal(new[] { "main", "loop", "done" }, functionSymbols.Select(s => s.Name).ToArray());
        Assert.DoesNotContain(functionSymbols, s => s.Name == "cnt");
        Assert.DoesNotContain(r.Debug.Functions, f => f.Name == "cnt");
    }

    [Fact]
    public void Sq_SingleLabelIsFunction_Entry0End3()
    {
        var r = Assembler.Assemble("sq.st16", TestSource.Sq);
        TestSource.AssertSucceeded(r);

        Assert.Equal(
            new (string, int, int)[] { ("sq", 0, 3) },
            r.Debug.Functions.Select(f => (f.Name, f.Entry, f.End)).ToArray());

        Assert.Contains(r.Debug.Symbols, s => s.Name == "sq" && s.Kind == "label" && s.Address == 0);
        Assert.Contains(r.Debug.Symbols, s => s.Name == "sq" && s.Kind == "function" && s.Address == 0);
    }

    [Fact]
    public void Json_WriteRead_RoundTrips()
    {
        var r = Assembler.Assemble("star.st16", TestSource.Starloop);
        TestSource.AssertSucceeded(r);

        string p1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".st16d");
        string p2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".st16d");
        try
        {
            r.Debug.WriteJson(p1);
            var back = St16dDebugFile.ReadJson(p1);

            Assert.Equal("st16d", back.Format);
            Assert.Equal(1, back.Version);
            Assert.Equal(r.Debug.Origin, back.Origin);
            Assert.Equal(r.Debug.Lines.Count, back.Lines.Count);
            Assert.Equal(
                (r.Debug.Lines[14].File, r.Debug.Lines[14].Line, r.Debug.Lines[14].Address),
                (back.Lines[14].File, back.Lines[14].Line, back.Lines[14].Address));
            Assert.Equal(r.Debug.Symbols[0].Name, back.Symbols[0].Name);
            Assert.Equal(r.Debug.Functions[0].End, back.Functions[0].End);

            // A second serialization of the read-back file must be byte-identical.
            back.WriteJson(p2);
            Assert.Equal(File.ReadAllText(p1), File.ReadAllText(p2));
        }
        finally
        {
            if (File.Exists(p1)) File.Delete(p1);
            if (File.Exists(p2)) File.Delete(p2);
        }
    }
}
