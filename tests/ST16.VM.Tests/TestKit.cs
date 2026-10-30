using ST16.VM;
using Xunit;

namespace ST16.VM.Tests;

/// <summary>
/// Shared test helpers: a machine wired to in-memory I/O so every test is
/// deterministic. (Spec: a VM is a pure function of (program, input).)
/// </summary>
public sealed class TestVm
{
    public QueueInputSource Input { get; } = new();
    public StringOutputSink Output { get; } = new();
    public ST16Machine Machine { get; }

    public TestVm(St16Config? config = null)
        => Machine = new ST16Machine(Input, Output, config);

    /// <summary>Loads a word image at 0x0000 (resets the machine) and returns this for chaining.</summary>
    public TestVm Load(params ushort[] words)
    {
        Machine.Load(words);
        return this;
    }

    /// <summary>Queues a console-input byte (port 0xF001 feed).</summary>
    public TestVm Enqueue(byte value)
    {
        Input.Enqueue(value);
        return this;
    }

    /// <summary>Top of the operand stack (s1); 0 when the stack is empty.</summary>
    public ushort Top => Machine.OperandStack[0];

    /// <summary>Occupied operand-stack count (the public stack list is full capacity).</summary>
    public int OperandCount => Machine.TakeSnapshot().OperandCount;

    /// <summary>The occupied operand stack, top first (real zeros are unambiguous here).</summary>
    public ushort[] StackTopDown()
    {
        var s = Machine.TakeSnapshot();
        return s.OperandStackTopDown;
    }

    /// <summary>Asserts the machine halted; returns the machine for chaining in test bodies.</summary>
    public TestVm ExpectHalted()
    {
        Assert.Equal(MachineState.Halted, Machine.State);
        return this;
    }

    /// <summary>Asserts the machine faulted with the given code; returns the machine.</summary>
    public TestVm ExpectFault(FaultCode code, ushort? faultPc = null)
    {
        Assert.Equal(MachineState.Faulted, Machine.State);
        Assert.Equal(code, Machine.LastFault);
        if (faultPc is { } expected)
            Assert.Equal(expected, Machine.FaultPc);
        return this;
    }
}
