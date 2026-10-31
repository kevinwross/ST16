using ST16.Assembler;
using ST16.CC;
using ST16.VM;
using Xunit;

namespace ST16.Samples.Tests;

/// <summary>
/// End-to-end tests for the teaching samples in <c>samples/</c>: each
/// sample is assembled (assembly) or compiled (C profile) from its
/// source and run on the reference VM with in-memory I/O; the console
/// output must match the sample's documented expectation exactly.
/// </summary>
public class SampleTests
{
    /// <summary>
    /// One sample: directory name, source file, expected console output,
    /// and optional input bytes.
    /// </summary>
    private readonly record struct Entry(
        string Dir, string File, string Expected, byte[]? Input);

    private static readonly Entry[] Samples =
    {
        new("01-hello",     "hello.st16",     "Hello, ST16!\n", null),
        new("02-sq",        "sq.st16",        "1",             null),
        new("03-starloop",  "starloop.st16",  "**********",    null),
        new("04-fib",       "fib.st16",       "55 55\n",       null),
        new("05-bubble",    "bubble.st16",    "11345\n",       null),
        new("06-calc",      "calc.st16",      "56\n",          Ascii("7*8=")),
        new("07-factorial", "factorial.c",    "720\n",         null),
        new("08-gcd",       "gcd.c",          "good\n",        null),
        new("09-stringrev", "stringrev.c",    "edcba\n",       null),
        new("10-binsearch", "binsearch.c",    "ok\n",          null),
        new("11-guess",     "guess.c",        "LHY\n",         Ascii("397")),
    };

    /// <summary>
    /// The repo root is the nearest ancestor of the test binary's
    /// directory that contains <c>ST16.slnx</c>.
    /// </summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ST16.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("ST16.slnx not found above " + AppContext.BaseDirectory);
    }

    private static string SourcePath(Entry e) =>
        Path.Combine(RepoRoot(), "samples", e.Dir, e.File);

    private static byte[] Ascii(string s) => System.Text.Encoding.ASCII.GetBytes(s);

    /// <summary>Builds the sample's word image from its source file.</summary>
    private static ushort[] Build(Entry e)
    {
        var text = File.ReadAllText(SourcePath(e));
        if (e.File.EndsWith(".st16"))
            return St16Assembler.Assemble(text, e.File).Words.ToArray();
        return CCCompiler.Compile(text, e.File).Assembly.Words.ToArray();
    }

    private static (string Output, ST16Machine Machine) Run(Entry e)
    {
        var output = new StringOutputSink();
        var inputSource = new QueueInputSource();
        if (e.Input is not null)
            foreach (var b in e.Input)
                inputSource.Enqueue(b);
        var machine = new ST16Machine(inputSource, output);
        machine.Load(Build(e));
        machine.Run();
        return (output.Text, machine);
    }

    [Theory]
    [InlineData("01-hello")]
    [InlineData("02-sq")]
    [InlineData("03-starloop")]
    [InlineData("04-fib")]
    [InlineData("05-bubble")]
    [InlineData("06-calc")]
    [InlineData("07-factorial")]
    [InlineData("08-gcd")]
    [InlineData("09-stringrev")]
    [InlineData("10-binsearch")]
    [InlineData("11-guess")]
    public void Sample_Produces_Expected_Output(string dir)
    {
        var entry = Samples.First(s => s.Dir == dir);
        var (text, machine) = Run(entry);
        Assert.Equal(MachineState.Halted, machine.State);
        Assert.Equal(entry.Expected, text);
    }

    [Fact]
    public void All_Samples_Directories_Are_Covered()
    {
        var samplesDir = Path.Combine(RepoRoot(), "samples");
        var dirs = Directory.GetDirectories(samplesDir)
            .Select(Path.GetFileName)
            .Where(n => n is not null && n.Length > 3
                && n[0] is >= '0' and <= '9'
                && n[1] is >= '0' and <= '9'
                && n[2] == '-')
            .Cast<string>()
            .OrderBy(n => n)
            .ToList();
        Assert.Equal(Samples.Select(s => s.Dir).OrderBy(d => d).ToList(), dirs);
    }
}
