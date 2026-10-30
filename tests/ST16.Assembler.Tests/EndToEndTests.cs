using ST16.VM;
using Xunit;

namespace ST16.Assembler.Tests;

/// <summary>
/// End to end: assembled images run on the reference ST16 engine.
/// </summary>
public class EndToEndTests
{
    [Fact]
    public void Starloop_AssembledWords_RunOnVm_HaltedPc0x17_121Instructions()
    {
        var result = St16Assembler.Assemble(Programs.Starloop, "starloop.st16");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var output = new StringOutputSink();
        var machine = new ST16Machine(new QueueInputSource(), output);
        machine.Load(result.Words.ToArray());
        machine.Run();

        Assert.Equal(MachineState.Halted, machine.State);
        Assert.Equal((ushort)0x0017, machine.Pc);
        Assert.Equal(121L, machine.InstructionCount);
        Assert.Equal("**********", output.Text); // ten asterisks on the console port
    }

    [Fact]
    public void Sq_AssembledProgram_ComputesSquare()
    {
        const string source = "    PUSH 7\n    CALL sq\n    HALT\nsq:\n    DUP\n    MUL\n    RET\n";
        var result = St16Assembler.Assemble(source, "sq.st16");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));

        var machine = new ST16Machine(new QueueInputSource(), new StringOutputSink());
        machine.Load(result.Words.ToArray());
        machine.Run();

        Assert.Equal(MachineState.Halted, machine.State);
        Assert.Equal((ushort)49, machine.OperandStack[0]);
    }
}
