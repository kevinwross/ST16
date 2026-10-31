using ST16.Assembler;

namespace ST16.CC;

/// <summary>
/// The st16cc compilation pipeline (plan P3.5): .c → ST16 assembly →
/// <see cref="ST16.Assembler.St16Assembler"/> → binary + <c>.st16d</c>.
/// The assembler's <c>.st16d</c> is post-processed: <c>sources[]</c> is
/// replaced by the .c file, and <c>lines[]</c> is remapped from the
/// generated-assembly line numbers to the .c line numbers via the
/// <c>; L&lt;line&gt;</c> markers the codegen emits.
/// </summary>
public static class CCCompiler
{
    /// <summary>The result of compiling one .c file.</summary>
    public sealed class Output
    {
        /// <summary>The generated ST16 assembly (with <c>; L&lt;line&gt;</c> markers).</summary>
        public string Asm { get; init; } = string.Empty;

        /// <summary>The assembler result (words, diagnostics).</summary>
        public AssembleResult Assembly { get; init; } = null!;

        /// <summary>The post-processed .st16d (sources = the .c file; lines → .c).</summary>
        public St16dFile St16d { get; init; } = null!;

        /// <summary>The binary image (little-endian words), 0x0000 through the last written word.</summary>
        public byte[] Binary => Assembly.ToBinary();
    }

    /// <summary>Compiles one .c source file (throws <see cref="CCException"/> on the first error).</summary>
    public static Output Compile(string source, string fileName)
    {
        var tokens = new CLexer(source).Lex();
        var program = new CParser(tokens).ParseProgram();
        var (functions, globals, locals) = new CSemantics(program).Analyze();
        string asm = new CCodegen(program, functions, globals, locals).Generate();

        var result = St16Assembler.Assemble(asm, Path.GetFileName(fileName) + ".st16");
        if (!result.Success)
        {
            var first = result.Diagnostics.FirstOrDefault(d => d.Severity == Severity.Error);
            throw new CCException(new CCDiagnostic(0, 0,
                first is null
                    ? "the generated assembly failed to assemble"
                    : $"the generated assembly failed to assemble (assembler line {first.Line}): {first.Message}"));
        }

        return new Output
        {
            Asm = asm,
            Assembly = result,
            St16d = PostProcessSt16d(result.DebugSymbols!, asm, source, Path.GetFileName(fileName)),
        };
    }

    /// <summary>
    /// Remaps the assembler's .st16d to the .c file: the sources become the
    /// .c text and every line entry's line number is the nearest preceding
    /// <c>; L&lt;line&gt;</c> marker in the generated assembly.
    /// </summary>
    public static St16dFile PostProcessSt16d(St16dFile baseDoc, string asm, string cSource, string cName)
    {
        string[] asmLines = asm.Split('\n');
        var map = new int[asmLines.Length + 1];
        int current = 1;
        for (int i = 0; i < asmLines.Length; i++)
        {
            string t = asmLines[i].TrimStart();
            if (t.StartsWith("; L", StringComparison.Ordinal) && int.TryParse(t.AsSpan(3), out int n))
                current = n;
            map[i + 1] = current;
        }

        var lines = new List<LineInfo>(baseDoc.Lines.Count);
        foreach (var li in baseDoc.Lines)
            lines.Add(new LineInfo(li.Address, 0, li.Line >= 1 && li.Line < map.Length ? map[li.Line] : 1));

        return new St16dFile
        {
            Version = 1,
            Sources = new[] { new SourceFile(cName, cSource) },
            Lines = lines,
            Symbols = baseDoc.Symbols,
            Functions = baseDoc.Functions,
            Sections = baseDoc.Sections,
        };
    }
}
