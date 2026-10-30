using ST16.VM;
using Xunit;

namespace ST16.VM.Tests;

/// <summary>
/// The spec §9 golden examples, word for word — the acceptance baseline
/// (spec §12).
/// </summary>
public class GoldenTests
{
    // Spec §9.2/§9.3: the starloop — print ten asterisks with a RAM counter.
    // 25 words at 0x0000–0x0018 (0x0018 = the counter word).
    private static readonly ushort[] StarLoop =
    {
        0x1B09, // 0x0000 PUSH 9
        0x1C00, // 0x0001 PUSH16
        0x0018, // 0x0002   cnt
        0x1E00, // 0x0003 STORE            mem[cnt] = 9
        0x1C00, // 0x0004 PUSH16
        0x002A, // 0x0005   0x2A           '*'
        0x0000, // 0x0006 DUP              (loop top)
        0x1C00, // 0x0007 PUSH16
        0xF000, // 0x0008   0xF000
        0x1E00, // 0x0009 STORE            print
        0x1C00, // 0x000A PUSH16
        0x0018, // 0x000B   cnt
        0x1D00, // 0x000C LOAD
        0x0000, // 0x000D DUP
        0x2207, // 0x000E JZ done          (offset +7 → 0x0016)
        0x1B01, // 0x000F PUSH 1
        0x0600, // 0x0010 SUB
        0x1C00, // 0x0011 PUSH16
        0x0018, // 0x0012   cnt
        0x1E00, // 0x0013 STORE
        0x1F00, // 0x0014 JMP
        0x0006, // 0x0015   loop
        0x0400, // 0x0016 DROP             (done)
        0x2400, // 0x0017 HALT
        0x0000, // 0x0018 .dw 0            cnt
    };

    [Fact]
    public void StarLoop_Golden()
    {
        var vm = new TestVm().Load(StarLoop);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new string('*', 10), vm.Output.Text);
        Assert.Equal(121, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x0017, vm.Machine.Pc);
        Assert.Equal((ushort)0, vm.Machine.ReadMemory(0x0018));
        Assert.Equal(RunStopReason.Halted, vm.Machine.LastRunStop);
    }

    [Fact]
    public void StarLoop_WithBreakpointAtLoopTop_StopsBeforeFirstDup()
    {
        var vm = new TestVm().Load(StarLoop);
        vm.Machine.SetBreakpoint(0x0006);
        var hits = new List<ushort>();
        vm.Machine.BreakpointHit += a => hits.Add(a);
        vm.Machine.Run();
        Assert.Equal(MachineState.Paused, vm.Machine.State);
        Assert.Equal(RunStopReason.Breakpoint, vm.Machine.LastRunStop);
        Assert.Equal((ushort)0x0006, vm.Machine.Pc);
        Assert.Equal(4, vm.Machine.InstructionCount); // prologue only; DUP not executed
        Assert.Equal(new[] { (ushort)0x0006 }, hits);
    }

    [Fact]
    public void StarLoop_ResumeAfterBreakpoint_CompletesGolden()
    {
        var vm = new TestVm().Load(StarLoop);
        vm.Machine.SetBreakpoint(0x0006);
        vm.Machine.Run();            // stops at 0x0006, ic 4
        vm.Machine.ClearBreakpoint(0x0006);
        vm.Machine.Run();            // runs the rest of the program
        vm.ExpectHalted();
        Assert.Equal(new string('*', 10), vm.Output.Text);
        Assert.Equal(121, vm.Machine.InstructionCount);
    }

    // Spec §9.1: the sq callee (x → x*x), assembled so execution starts at 0x0000.
    //   0x0000 PUSH 7        0x0004 DUP
    //   0x0001 CALL          0x0005 MUL
    //   0x0002   0x0004      0x0006 RET
    //   0x0003 HALT
    private static readonly ushort[] SqProgram = { 0x1B07, 0x2000, 0x0004, 0x2400, 0x0000, 0x0800, 0x2100 };

    [Fact]
    public void Sq_Golden()
    {
        var vm = new TestVm().Load(SqProgram);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)49, vm.Top);
        Assert.Equal(0, vm.Machine.CallDepth);
        Assert.Equal(6, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x0003, vm.Machine.Pc);
    }

    [Fact]
    public void Sq_StepOverCallFrame_StopsJustAfterRet()
    {
        var vm = new TestVm().Load(SqProgram);
        Assert.True(vm.Machine.StepInto());   // PUSH 7
        Assert.Equal(1, vm.Machine.InstructionCount);
        Assert.True(vm.Machine.StepOver());   // CALL runs the whole sq frame
        Assert.Equal(5, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x0003, vm.Machine.Pc);
        Assert.Equal((ushort)49, vm.Top);
        Assert.Equal(0, vm.Machine.CallDepth);
        Assert.Equal(MachineState.Paused, vm.Machine.State);
    }
}
