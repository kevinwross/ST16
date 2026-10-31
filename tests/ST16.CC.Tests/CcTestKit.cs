using ST16.CC;
using ST16.VM;
using Xunit;

namespace ST16.CC.Tests;

/// <summary>
/// Shared helpers: compile a C source and run the binary on the reference
/// ST16 engine with in-memory I/O (deterministic, spec: a VM is a pure
/// function of (program, input)).
/// </summary>
public static class CcTestKit
{
    /// <summary>Compiles C source; returns the pipeline output (throws CCException on error).</summary>
    public static CCCompiler.Output Compile(string source, string name = "test.c")
        => CCCompiler.Compile(source, name);

    /// <summary>
    /// Compiles and runs. Returns (console output, machine, pipeline output).
    /// </summary>
    public static (string Output, ST16Machine Machine, CCCompiler.Output Compiled) Run(
        string source, byte[]? input = null, string name = "test.c")
    {
        var compiled = CCCompiler.Compile(source, name);
        var output = new StringOutputSink();
        var inputSource = new QueueInputSource();
        if (input is not null)
            foreach (var b in input)
                inputSource.Enqueue(b);
        var machine = new ST16Machine(inputSource, output);
        machine.Load(compiled.Assembly.Words.ToArray());
        machine.Run();
        return (output.Text, machine, compiled);
    }

    /// <summary>Asserts a program halted and printed exactly the expected text.</summary>
    public static void ExpectOutput(string source, string expected, byte[]? input = null)
    {
        var (text, machine, _) = Run(source, input);
        Assert.Equal(MachineState.Halted, machine.State);
        Assert.Equal(expected, text);
    }

    /// <summary>Asserts compilation fails and the diagnostic message contains <paramref name="messagePart"/>.</summary>
    public static void ExpectCompileError(string source, string messagePart)
    {
        var ex = Assert.Throws<CCException>(() => CCCompiler.Compile(source, "test.c"));
        Assert.Contains(messagePart, ex.Diagnostic.Message);
    }
}
