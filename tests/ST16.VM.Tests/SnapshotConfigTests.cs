using ST16.VM;
using Xunit;

namespace ST16.VM.Tests;

/// <summary>
/// Snapshot/restore, determinism, and configurable stack depths
/// (4–256, default 32/32; strict mode).
/// </summary>
public class SnapshotConfigTests
{
    private static readonly ushort[] StarLoop =
    {
        0x1B09, 0x1C00, 0x0018, 0x1E00, 0x1C00, 0x002A, 0x0000, 0x1C00, 0xF000, 0x1E00,
        0x1C00, 0x0018, 0x1D00, 0x0000, 0x2207, 0x1B01, 0x0600, 0x1C00, 0x0018, 0x1E00,
        0x1F00, 0x0006, 0x0400, 0x2400, 0x0000,
    };

    [Fact]
    public void Snapshot_RoundTrip_MidRun_RestoresExactState()
    {
        var vm = new TestVm().Load(StarLoop);
        for (int i = 0; i < 10; i++)
            vm.Machine.StepInto();

        var snapshot = vm.Machine.TakeSnapshot();
        Assert.Equal(10, snapshot.InstructionCount);
        // After 10 starloop instructions (PUSH, PUSH16, STORE, PUSH16, DUP,
        // PUSH16, STORE, PUSH16, LOAD, DUP) the next fetch is at 0x000E.
        Assert.Equal((ushort)0x000E, snapshot.Pc);

        // Run to completion, record the result.
        vm.Machine.Run();
        Assert.Equal(new string('*', 10), vm.Output.Text);
        Assert.Equal(121, vm.Machine.InstructionCount);
        var finishedPc = vm.Machine.Pc;

        // Restore the mid-run snapshot and run again — the outcome must be identical.
        // (The output sink is cleared first; the restored run produces the 9 stars
        // still owed from ic 10 onward — the 10th star was already emitted before
        // the snapshot, in the first run.)
        vm.Output.Clear();
        vm.Machine.Restore(snapshot);
        Assert.Equal(10, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x000E, vm.Machine.Pc);
        Assert.Equal(MachineState.Paused, vm.Machine.State);

        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new string('*', 9), vm.Output.Text);
        Assert.Equal(121, vm.Machine.InstructionCount);
        Assert.Equal(finishedPc, vm.Machine.Pc);
        Assert.Equal((ushort)0, vm.Machine.ReadMemory(0x0018));
    }

    [Fact]
    public void Snapshot_RestoreAcrossDifferentMachineInstances()
    {
        var a = new TestVm().Load(StarLoop);
        for (int i = 0; i < 13; i++)
            a.Machine.StepInto();
        var snapshot = a.Machine.TakeSnapshot();

        var b = new TestVm(); // fresh machine, same config
        b.Machine.Restore(snapshot);
        Assert.Equal(13, b.Machine.InstructionCount);
        Assert.Equal(snapshot.Pc, b.Machine.Pc);
        Assert.Equal(snapshot.OperandStackTopDown, b.Machine.TakeSnapshot().OperandStackTopDown);

        b.Machine.Run();
        b.ExpectHalted();
        // B's fresh output sink holds only the 9 stars emitted after ic 13
        // (the 10th star was emitted by machine A before the snapshot).
        Assert.Equal(new string('*', 9), b.Output.Text);
        Assert.Equal(121, b.Machine.InstructionCount);
    }

    [Fact]
    public void Restore_DepthMismatch_Throws()
    {
        var a = new TestVm(new St16Config(8, 32)).Load(StarLoop);
        for (int i = 0; i < 5; i++)
            a.Machine.StepInto();
        var snapshot = a.Machine.TakeSnapshot();

        var b = new TestVm(); // 32/32
        Assert.Throws<ArgumentException>(() => b.Machine.Restore(snapshot));
    }

    [Fact]
    public void Snapshot_BreakpointsNotCarried()
    {
        // Breakpoints/watchpoints are debugger settings, not machine state.
        var a = new TestVm().Load(StarLoop);
        a.Machine.SetBreakpoint(0x0006);
        a.Machine.SetWatchpoint(0x0018);
        var snapshot = a.Machine.TakeSnapshot();

        var b = new TestVm();
        b.Machine.Restore(snapshot);
        Assert.Empty(b.Machine.Breakpoints);
        Assert.Empty(b.Machine.Watchpoints);
        Assert.True(a.Machine.HasBreakpoint(0x0006)); // source machine keeps its settings
    }

    [Fact]
    public void Determinism_TwoMachinesSameProgramSameInput_IdenticalTraces()
    {
        (List<(long, ushort, Opcode, string)> Trace, MachineState State, ushort Pc, long Ic, ushort Top) RunFresh()
        {
            var input = new QueueInputSource();
            input.Enqueue(0x41); // identical input sequence on both machines
            var m = new ST16Machine(input, new StringOutputSink());
            m.Load(StarLoop);
            var trace = new List<(long, ushort, Opcode, string)>();
            m.InstructionCompleted += e => trace.Add((e.Index, e.Pc, e.Instruction.Opcode, e.Instruction.Mnemonic));
            m.Run();
            return (trace, m.State, m.Pc, m.InstructionCount, m.OperandStack[0]);
        }

        var (t1, s1, pc1, ic1, top1) = RunFresh();
        var (t2, s2, pc2, ic2, top2) = RunFresh();

        Assert.Equal(t1, t2); // identical instruction-by-instruction trace
        Assert.Equal(121, t1.Count);
        Assert.Equal((s1, pc1, ic1, top1), (s2, pc2, ic2, top2)); // identical final state
        Assert.Equal(MachineState.Halted, s1);
        Assert.Equal(121L, ic1);
    }

    [Fact]
    public void Determinism_SameInputBytes_SameOutput()
    {
        // A program that reads one byte and echoes it (LOAD 0xF001 → STORE 0xF000).
        var program = new ushort[] { 0x1C00, 0xF001, 0x1D00, 0x1C00, 0xF000, 0x1E00, 0x2400 };

        TestVm Run()
        {
            var vm = new TestVm();
            vm.Enqueue(0x41);
            vm.Load(program);
            vm.Machine.Run();
            return vm;
        }

        var m1 = Run();
        var m2 = Run();
        Assert.Equal(m1.Output.Text, m2.Output.Text);
        Assert.Equal("A", m1.Output.Text);
        Assert.Equal(m1.Machine.InstructionCount, m2.Machine.InstructionCount);
        Assert.Equal(MachineState.Halted, m1.Machine.State);
    }

    // ---- Configurable depths ----

    [Fact]
    public void Config_DefaultsAre32By32()
    {
        var config = new St16Config();
        Assert.Equal((ushort)32, config.OperandDepth);
        Assert.Equal((ushort)32, config.CallDepth);
    }

    [Theory]
    [InlineData(3, 32)]
    [InlineData(32, 257)]
    [InlineData(1, 1)]
    [InlineData(256, 0)]
    public void Config_OutOfRange_DepthsThrow(ushort operand, ushort call)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new St16Config(operand, call));
    }

    [Theory]
    [InlineData(4, 256)]
    [InlineData(256, 4)]
    [InlineData(16, 16)]
    public void Config_BoundariesAccepted(ushort operand, ushort call)
    {
        var config = new St16Config(operand, call);
        Assert.Equal(operand, config.OperandDepth);
        Assert.Equal(call, config.CallDepth);
    }

    [Fact]
    public void Config_StrictMode_Depth4_FaultsWhereDepth32DoesNot()
    {
        var words = new ushort[] { 0x1B01, 0x1B01, 0x1B01, 0x1B01, 0x1B01, 0x2400 };

        var relaxed = new TestVm().Load(words);
        relaxed.Machine.Run();
        relaxed.ExpectHalted(); // five values fit in 32 slots

        var strict = new TestVm(new St16Config(4, 4)).Load(words);
        strict.Machine.Run();
        strict.ExpectFault(FaultCode.StackOf, 4);
    }

    [Fact]
    public void Config_StrictCallDepth4_CallofOnFifthNestedCall()
    {
        // Self-recursive CALL: with call depth 4 the 5th frame overflows.
        var vm = new TestVm(new St16Config(32, 4)).Load(0x2000, 0x0000);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.CallOf, 0x0000);
        Assert.Equal(4, vm.Machine.InstructionCount);
        Assert.Equal(4, vm.Machine.CallDepth);
    }
}
