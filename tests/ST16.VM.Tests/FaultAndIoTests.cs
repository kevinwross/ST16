namespace ST16.VM.Tests;

/// <summary>Spec §6 fault matrix (atomicity: no partial effects) + §7 I/O ports.</summary>
public class FaultAndIoTests
{
    [Fact]
    public void Div0_DivisorTopZero_NoPartialEffects()
    {
        var m = TestKit.Step1(Op.Div, 0, 5, 0);
        Assert.Equal(MachineState.Faulted, m.State);
        Assert.Equal(ST16Machine.FaultDiv0, m.FaultCode);
        Assert.Equal((ushort)0, m.FaultPC);
        TestKit.AssertStack(m, 0, 5);               // unchanged
    }

    [Fact]
    public void Div0_ModAlsoFaults()
    {
        var m = TestKit.Step1(Op.Mod, 0, 5, 0);
        Assert.Equal(ST16Machine.FaultDiv0, m.FaultCode);
    }

    [Fact]
    public void StackUf_LoadWithEmptyStack()
    {
        var m = TestKit.Step1(Op.Load, 0);
        Assert.Equal(ST16Machine.FaultStackUf, m.FaultCode);
    }

    [Fact]
    public void Illop_ReservedOpcode()
    {
        var m = TestKit.Step1(0x25, 0);
        Assert.Equal(ST16Machine.FaultIllop, m.FaultCode);

        var m2 = TestKit.Step1(0x3F, 0x12);
        Assert.Equal(ST16Machine.FaultIllop, m2.FaultCode);
    }

    [Fact]
    public void Illop_Bits15_14Set()
    {
        // header 0xC000: bits 15–14 set -> ILLOP even though the low bits decode like DUP
        var m = TestKit.Machine(new ushort[] { 0xC000 });
        m.Step();
        Assert.Equal(MachineState.Faulted, m.State);
        Assert.Equal(ST16Machine.FaultIllop, m.FaultCode);
    }

    [Fact]
    public void AddrF_UnmappedPortLoad()
    {
        var m = TestKit.Step1(Op.Load, 0, 0xF003);
        Assert.Equal(ST16Machine.FaultAddrF, m.FaultCode);
        TestKit.AssertStack(m, 0xF003);             // unchanged
    }

    [Fact]
    public void AddrF_UnmappedPortStore_NoPartialEffects()
    {
        var m = TestKit.Step1(Op.Store, 0, 0x1234, 0xF005);
        Assert.Equal(ST16Machine.FaultAddrF, m.FaultCode);
        TestKit.AssertStack(m, 0xF005, 0x1234);     // both values still on stack
    }

    [Fact]
    public void CallOf_CallStackFull()
    {
        var cfg = new ST16Config { CallStackDepth = 4 };
        var m = new ST16Machine(cfg);
        m.LoadProgram(new ushort[] { TestKit.Encode(Op.Call), 0x0002 });
        TestKit.SeedCallStack(m, 0x11, 0x22, 0x33, 0x44);     // full
        m.Step();
        Assert.Equal(MachineState.Faulted, m.State);
        Assert.Equal(ST16Machine.FaultCallOf, m.FaultCode);
        Assert.Equal((ushort)0, m.PC);              // PC unchanged
        Assert.Equal(4, m.CallStack.Count);         // unchanged
    }

    [Fact]
    public void StoreToConsoleOut_EmitsLowByte()
    {
        var output = new InMemoryOutput();
        var m = new ST16Machine(output: output);
        m.LoadProgram(new[] { TestKit.Encode(Op.Push, 0x41), TestKit.Encode(Op.Push16), (ushort)0xF000, TestKit.Encode(Op.Store), TestKit.Encode(Op.Halt) });
        m.Run();
        Assert.Equal("A", output.AsText());
        Assert.Equal(MachineState.Halted, m.State);
    }

    [Fact]
    public void StoreToConsoleOut_IgnoresHighByte()
    {
        var output = new InMemoryOutput();
        var m = new ST16Machine(output: output);
        m.LoadProgram(new[] { TestKit.Encode(Op.Push16), (ushort)0x1241, TestKit.Encode(Op.Push16), (ushort)0xF000, TestKit.Encode(Op.Store), TestKit.Encode(Op.Halt) });
        m.Run();
        Assert.Equal("A", output.AsText());
    }

    [Fact]
    public void LoadConsoleOut_ReturnsZero()
    {
        var m = TestKit.Step1(Op.Load, 0, 0xF000);
        Assert.Equal(MachineState.Running, m.State);
        TestKit.AssertStack(m, 0);
    }

    [Fact]
    public void LoadConsoleIn_NonBlockingEmptyGives0xFFFF()
    {
        var m = TestKit.Step1(Op.Load, 0, 0xF001);     // default EmptyInputSource
        TestKit.AssertStack(m, 0xFFFF);
    }

    [Fact]
    public void LoadConsoleIn_ReturnsQueuedCharsThenEmpty()
    {
        var input = new InMemoryInput("hi");
        var m = new ST16Machine(input: input);
        m.LoadProgram(new[]
        {
            TestKit.Encode(Op.Push16), (ushort)0xF001, TestKit.Encode(Op.Load),
            TestKit.Encode(Op.Push16), (ushort)0xF001, TestKit.Encode(Op.Load),
            TestKit.Encode(Op.Push16), (ushort)0xF001, TestKit.Encode(Op.Load),
            TestKit.Encode(Op.Halt),
        });
        m.Run();
        TestKit.AssertStack(m, 0xFFFF, 0x0069, 0x0068); // top = most recent read; 'h' high byte 0
    }

    [Fact]
    public void LoadCounter_ReturnsInstructionCount()
    {
        // program: LOAD 0xF002 ; LOAD 0xF002 ; HALT — reads 0 then 1
        var m = TestKit.Machine(new[]
        {
            TestKit.Encode(Op.Push16), (ushort)0xF002, TestKit.Encode(Op.Load),
            TestKit.Encode(Op.Push16), (ushort)0xF002, TestKit.Encode(Op.Load),
            TestKit.Encode(Op.Halt),
        });
        m.Run();
        TestKit.AssertStack(m, 3, 1);                  // counter reads 1 then 3 mid-program
    }

    [Fact]
    public void StoreToConsoleInAndCounter_AreNoOps()
    {
        var m = TestKit.Step1(Op.Store, 0, (ushort)0x1234, (ushort)0xF001);
        Assert.Equal(MachineState.Running, m.State);
        TestKit.AssertStack(m);
        var m2 = TestKit.Step1(Op.Store, 0, 0x5678, 0xF002);
        Assert.Equal(MachineState.Running, m2.State);
    }
}
