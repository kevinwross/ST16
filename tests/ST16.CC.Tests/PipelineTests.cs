using ST16.Assembler;
using Xunit;

namespace ST16.CC.Tests;

/// <summary>
/// .st16d post-processing tests: the assembler's debug-symbol document is
/// remapped from generated-assembly lines to .c lines, and the source
/// list is replaced by the .c file.
/// </summary>
public class PipelineTests
{
    private const string Source =
        "int gcd(int a, int b);\n\n" +
        "int main()\n" +
        "{\n" +
        "    int r = gcd(48, 18);\n" +
        "    return r == 6 ? 0 : 1;\n" +
        "}\n\n" +
        "int gcd(int a, int b)\n" +
        "{\n" +
        "    while (b != 0)\n" +
        "    {\n" +
        "        int t = a % b;\n" +
        "        a = b;\n" +
        "        b = t;\n" +
        "    }\n" +
        "    return a;\n" +
        "}\n";

    [Fact]
    public void St16d_SourcesAreTheCFile()
    {
        var output = CCCompiler.Compile(Source, "t6.c");
        var st16d = output.St16d;
        Assert.Single(st16d.Sources);
        Assert.Equal("t6.c", st16d.Sources[0].Name);
        Assert.Equal(Source, st16d.Sources[0].Text);
    }

    [Fact]
    public void St16d_LinesAreRemappedToCSource()
    {
        var output = CCCompiler.Compile(Source, "t6.c");
        Assert.True(output.St16d.Lines.Count > 10, "expected many line entries");

        // Every remapped line must be a plausible .c line number.
        int sourceLines = Source.Split('\n').Length;
        foreach (var li in output.St16d.Lines)
            Assert.InRange(li.Line, 1, sourceLines);
    }

    [Fact]
    public void St16d_MainEntryPoint_MapsToItsLine()
    {
        var output = CCCompiler.Compile(Source, "t6.c");
        var main = Assert.Single(output.St16d.Functions, f => f.Name == "main");
        // The entry word's line entry must be the 'int main()' line (3).
        var entryLine = Assert.Single(output.St16d.Lines, l => l.Address == main.Entry);
        Assert.Equal(3, entryLine.Line);
    }

    [Fact]
    public void St16d_SymbolsCarriedThrough()
    {
        var output = CCCompiler.Compile(Source, "t6.c");
        var symbols = output.St16d.Symbols.Select(s => s.Name).ToList();
        Assert.Contains("main", symbols);
        Assert.Contains("gcd", symbols);
    }

    [Fact]
    public void St16d_VersionIsOne()
    {
        var output = CCCompiler.Compile(Source, "t6.c");
        Assert.Equal(1, output.St16d.Version);
    }

    [Fact]
    public void AssemblyWordCount_MatchesBinary()
    {
        var output = CCCompiler.Compile(Source, "t6.c");
        Assert.Equal(output.Assembly.Words.Count, output.Binary.Length / 2);
    }

    [Fact]
    public void GeneratedAssembly_ContainsLineMarkers()
    {
        var output = CCCompiler.Compile(Source, "t6.c");
        // Markers appear at statement boundaries; at least the main entry
        // region carries a line-3 marker.
        Assert.Contains("; L3", output.Asm);
    }
}
