namespace ST16.VM.Tests;

/// <summary>Spec §5.1 stack manipulation + §5.4 memory.</summary>
public class StackAndMemoryTests
{
    [Fact] public void Dup_CopiesTop() => TestKit.AssertStack(TestKit.Step1(Op.Dup, 0, 5), 5, 5);
    [Fact] public void Swap_ExchangesTopTwo() => TestKit.AssertStack(TestKit.Step1(Op.Swap, 0, 1, 2), 1, 2);
    [Fact] public void Over_CopiesSecondToTop() => TestKit.AssertStack(TestKit.Step1(Op.Over, 0, 1, 2), 1, 2, 1);  // a b -> a b a
    [Fact] public void Drop_DiscardsTop() => TestKit.AssertStack(TestKit.Step1(Op.Drop, 0, 1, 2), 1);

    [Fact]
    public void Rot_RotatesTopThree()
    {
        // a b c -> b c a with c on top: [1,2,3] -> bottom→top [2,3,1]
        TestKit.AssertStack(TestKit.Step1(Op.Rot, 0, 1, 2, 3), 1, 3, 2);
    }

    [Fact]
    public void Push_SignExtends8BitImmediate()
    {
        TestKit.AssertStack(TestKit.Step1(Op.Push, 10), 10);
        TestKit.AssertStack(TestKit.Step1(Op.Push, 0x7F), 0x007F);          // +127
        TestKit.AssertStack(TestKit.Step1(Op.Push, 0x80), 0xFF80);          // −128
        TestKit.AssertStack(TestKit.Step1(Op.Push, 0xFF), 0xFFFF);          // −1
    }

    [Fact]
    public void Push16_PushesFullWordFromNextWord()
    {
        var m = TestKit.Machine(new ushort[] { TestKit.Encode(Op.Push16), 0x1234 });
        m.Step();
        TestKit.AssertStack(m, 0x1234);
        Assert.Equal((ushort)2, m.PC);
    }

    [Fact]
    public void Load_PopsAddressPushesValue()
    {
        var m = TestKit.Machine(new[] { TestKit.Encode(Op.Load) }, 0x0010);
        m.WriteMemory(0x0010, 0xBEEF);
        m.Step();
        TestKit.AssertStack(m, 0xBEEF);
    }

    [Fact]
    public void Store_PopsAddressThenValue()
    {
        var m = TestKit.Step1(Op.Store, 0, 0xCAFE, 0x0020);   // value 0xCAFE, address 0x0020 on top
        Assert.Equal((ushort)0xCAFE, m.ReadMemory(0x0020));
        TestKit.AssertStack(m);
    }

    [Fact]
    public void StackOf_FaultsWhenFull()
    {
        var cfg = new ST16Config { OperandStackDepth = 4 };
        var m = new ST16Machine(cfg);
        m.LoadProgram(new[] { TestKit.Encode(Op.Push, 9) });
        TestKit.SeedCallStack(m); // no-op call-stack seed; seed operand stack via snapshot
        var snap = m.TakeSnapshot();
        m.Restore(new MachineSnapshot
        {
            PC = 0, State = MachineState.Running,
            OperandStack = new ushort[] { 1, 2, 3, 4 },   // full (depth 4), top first
            Memory = snap.Memory, Breakpoints = snap.Breakpoints,
        });
        Assert.True(m.Step());
        Assert.Equal(MachineState.Faulted, m.State);
        Assert.Equal(ST16Machine.FaultStackOf, m.FaultCode);
        TestKit.AssertStack(m, 1, 2, 3, 4);               // atomic: stack unchanged
    }

    [Fact]
    public void Dup_FaultsWhenEmpty()
    {
        var m = TestKit.Step1(Op.Dup, 0);
        Assert.Equal(MachineState.Faulted, m.State);
        Assert.Equal(ST16Machine.FaultStackUf, m.FaultCode);
    }

    [Fact]
    public void Swap_FaultsWithOneElement()
    {
        var m = TestKit.Step1(Op.Swap, 0, 1);
        Assert.Equal(ST16Machine.FaultStackUf, m.FaultCode);
        TestKit.AssertStack(m, 1);
    }

    [Fact]
    public void Drop_FaultsWhenEmpty()
    {
        var m = TestKit.Step1(Op.Drop, 0);
        Assert.Equal(ST16Machine.FaultStackUf, m.FaultCode);
    }
}
