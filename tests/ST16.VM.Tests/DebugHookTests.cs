using ST16.VM;
using Xunit;

namespace ST16.VM.Tests;

/// <summary>
/// Debug hooks (P2 checklist): stepping (Into/Over/Out), breakpoints,
/// write-only watchpoints, the instruction-limit watchdog, run control,
/// the InstructionCompleted/FaultOccurred/Halted events, and Load/Reset.
/// </summary>
public class DebugHookTests
{
    private static readonly ushort[] StarLoop =
    {
        0x1B09, 0x1C00, 0x0018, 0x1E00, 0x1C00, 0x002A, 0x0000, 0x1C00, 0xF000, 0x1E00,
        0x1C00, 0x0018, 0x1D00, 0x0000, 0x2207, 0x1B01, 0x0600, 0x1C00, 0x0018, 0x1E00,
        0x1F00, 0x0006, 0x0400, 0x2400, 0x0000,
    };

    // ---- Stepping ----

    [Fact]
    public void StepInto_OneInstruction_AtATime_IncludingTwoWordAdvance()
    {
        var vm = new TestVm().Load(StarLoop);
        Assert.True(vm.Machine.StepInto());
        Assert.Equal(1, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x0001, vm.Machine.Pc); // PUSH16 header
        Assert.True(vm.Machine.StepInto());
        Assert.Equal(2, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x0003, vm.Machine.Pc); // two-word skip
        Assert.Equal(MachineState.Paused, vm.Machine.State);
    }

    [Fact]
    public void StepInto_OnHaltedMachine_ReturnsFalseAndChangesNothing()
    {
        var vm = new TestVm().Load(0x2400);
        vm.Machine.Run();
        Assert.False(vm.Machine.StepInto());
        Assert.Equal(1, vm.Machine.InstructionCount);
    }

    [Fact]
    public void StepInto_OnFaultedMachine_ReturnsFalse()
    {
        var vm = new TestVm().Load(0x2100); // RET with empty call stack
        vm.Machine.Run();
        Assert.False(vm.Machine.StepInto());
        Assert.Equal(FaultCode.CallUf, vm.Machine.LastFault);
    }

    [Fact]
    public void StepOver_NonCallInstruction_EqualsStepInto()
    {
        var vm = new TestVm().Load(StarLoop);
        Assert.True(vm.Machine.StepOver()); // DUP? No — the first instruction is PUSH 9.
        Assert.Equal(1, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x0001, vm.Machine.Pc);
    }

    [Fact]
    public void StepOut_TopLevelBehavesAsStepInto()
    {
        var vm = new TestVm().Load(StarLoop);
        Assert.True(vm.Machine.StepOut()); // call depth 0 → one instruction
        Assert.Equal(1, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x0001, vm.Machine.Pc);
    }

    [Fact]
    public void StepOut_RunsToMatchingRet()
    {
        // main: PUSH 1, CALL f, HALT      f: CALL g, RET      g: PUSH 9, RET
        var vm = new TestVm().Load(
            0x1B01, 0x2000, 0x0005, 0x2400, // 0x0000–0x0003  main: PUSH 1, CALL 0x0005, HALT
            0x0000,                          // 0x0004          padding
            0x2000, 0x000A, 0x2100,         // 0x0005–0x0007   f: CALL 0x000A, RET
            0x0000, 0x0000,                  // 0x0008–0x0009   padding
            0x1B09, 0x2100                   // 0x000A–0x000B   g: PUSH 9, RET
        );
        Assert.True(vm.Machine.StepInto()); // PUSH 1
        Assert.True(vm.Machine.StepInto()); // CALL f
        Assert.True(vm.Machine.StepInto()); // CALL g
        Assert.Equal(3, vm.Machine.InstructionCount);
        Assert.Equal(2, vm.Machine.CallDepth);
        Assert.True(vm.Machine.StepOut()); // run g's frame: PUSH 9, RET
        Assert.Equal(5, vm.Machine.InstructionCount);
        Assert.Equal(1, vm.Machine.CallDepth);
        Assert.Equal((ushort)0x0007, vm.Machine.Pc); // f's RET
        Assert.Equal(MachineState.Paused, vm.Machine.State);
        Assert.True(vm.Machine.StepOut()); // run f's frame: RET
        Assert.Equal(6, vm.Machine.InstructionCount);
        Assert.Equal(0, vm.Machine.CallDepth);
        Assert.Equal((ushort)0x0003, vm.Machine.Pc); // HALT
    }

    // ---- Breakpoints ----

    [Fact]
    public void Breakpoint_MultipleSimultaneous_ClearAffectsOnlyOne()
    {
        var vm = new TestVm().Load(StarLoop);
        vm.Machine.SetBreakpoint(0x0006);
        vm.Machine.SetBreakpoint(0x0000);
        Assert.Equal(new ushort[] { 0x0000, 0x0006 }, vm.Machine.Breakpoints);
        vm.Machine.Run();
        Assert.Equal((ushort)0x0000, vm.Machine.Pc); // first breakpoint hit at start
        Assert.Equal(0, vm.Machine.InstructionCount);
        vm.Machine.ClearBreakpoint(0x0000);
        vm.Machine.Run();
        Assert.Equal((ushort)0x0006, vm.Machine.Pc);
        Assert.Equal(4, vm.Machine.InstructionCount);
        Assert.True(vm.Machine.HasBreakpoint(0x0006));
    }

    [Fact]
    public void BreakpointHit_FiresWithAddress()
    {
        var vm = new TestVm().Load(StarLoop);
        vm.Machine.SetBreakpoint(0x0006);
        var hits = new List<ushort>();
        vm.Machine.BreakpointHit += a => hits.Add(a);
        vm.Machine.Run();
        Assert.Equal(new[] { (ushort)0x0006 }, hits);
    }

    // ---- Watchpoints (write-only) ----

    [Fact]
    public void Watchpoint_StoreFires_LoadDoesNot()
    {
        var vm = new TestVm().Load(0x1B07, 0x1C00, 0x0010, 0x1E00, 0x1C00, 0x0010, 0x1D00, 0x2400);
        var hits = new List<ushort>();
        vm.Machine.WatchpointHit += a => hits.Add(a);
        vm.Machine.SetWatchpoint(0x0010);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new[] { (ushort)0x0010 }, hits); // the STORE only
    }

    [Fact]
    public void Watchpoint_DebugWriteMemory_Fires()
    {
        var vm = new TestVm();
        var hits = new List<ushort>();
        vm.Machine.WatchpointHit += a => hits.Add(a);
        vm.Machine.SetWatchpoint(0x0020);
        vm.Machine.WriteMemory(0x0020, 0x1234);
        Assert.Equal(new[] { (ushort)0x0020 }, hits);
        Assert.Equal((ushort)0x1234, vm.Machine.ReadMemory(0x0020));
    }

    [Fact]
    public void Watchpoint_Clear_StopsFiring()
    {
        var vm = new TestVm();
        var hits = new List<ushort>();
        vm.Machine.WatchpointHit += a => hits.Add(a);
        vm.Machine.SetWatchpoint(0x0020);
        vm.Machine.ClearWatchpoint(0x0020);
        vm.Machine.WriteMemory(0x0020, 0x1234);
        Assert.Empty(hits);
        Assert.False(vm.Machine.HasWatchpoint(0x0020));
        Assert.Empty(vm.Machine.Watchpoints);
    }

    // ---- Instruction-limit watchdog ----

    [Fact]
    public void Limit_StopIsPausedNotFaulted_ResumeAfterRaisingLimit()
    {
        var vm = new TestVm().Load(StarLoop);
        vm.Machine.InstructionLimit = 10;
        Assert.False(vm.Machine.AtInstructionLimit());
        vm.Machine.Run();
        Assert.Equal(MachineState.Paused, vm.Machine.State);
        Assert.Equal(RunStopReason.Limit, vm.Machine.LastRunStop);
        Assert.Equal(10, vm.Machine.InstructionCount);
        Assert.True(vm.Machine.AtInstructionLimit());
        vm.Machine.InstructionLimit = 20;
        vm.Machine.Run();
        Assert.Equal(20, vm.Machine.InstructionCount);
    }

    [Fact]
    public void Limit_ZeroMeansUnlimited()
    {
        var vm = new TestVm().Load(StarLoop);
        Assert.Equal(0, vm.Machine.InstructionLimit);
        Assert.False(vm.Machine.AtInstructionLimit());
        vm.Machine.Run();
        vm.ExpectHalted();
    }

    // ---- Events ----

    [Fact]
    public void InstructionCompleted_StarLoop_CountAndContent()
    {
        var vm = new TestVm().Load(StarLoop);
        var events = new List<InstructionCompletedEventArgs>();
        vm.Machine.InstructionCompleted += e => events.Add(e);
        vm.Machine.Run();
        vm.ExpectHalted();

        Assert.Equal(121, events.Count);
        Assert.All(events, e => Assert.False(e.Index < 0 || e.Index > 120));

        var first = events[0];
        Assert.Equal(0, first.Index);
        Assert.Equal((ushort)0x0000, first.Pc);
        Assert.Equal(Opcode.Push, first.Instruction.Opcode);
        Assert.Equal((ushort)9, first.Instruction.Operand);
        Assert.Null(first.Instruction.SecondWord);
        Assert.Equal("PUSH", first.Instruction.Mnemonic);

        var push16 = events[1]; // PUSH16 cnt at 0x0001
        Assert.Equal((ushort)0x0001, push16.Pc);
        Assert.Equal(Opcode.Push16, push16.Instruction.Opcode);
        Assert.Equal((ushort)0x0018, push16.Instruction.SecondWord);

        var last = events[^1];
        Assert.Equal("HALT", last.Instruction.Mnemonic);
        Assert.Equal((ushort)0x0017, last.Pc);
        Assert.Equal(120, last.Index);
    }

    [Fact]
    public void FaultOccurred_FiresWithCodeAndPc()
    {
        var vm = new TestVm().Load(0x1B05, 0x1B00, 0x0900, 0x2400);
        FaultCode? code = null;
        ushort? pc = null;
        vm.Machine.FaultOccurred += (c, p) => { code = c; pc = p; };
        vm.Machine.Run();
        Assert.Equal(FaultCode.Div0, code);
        Assert.Equal((ushort)0x0002, pc);
    }

    [Fact]
    public void Halted_FiresWithTerminationCodeZero()
    {
        var vm = new TestVm().Load(0x2400);
        ushort? code = null;
        vm.Machine.Halted += c => code = c;
        vm.Machine.Run();
        Assert.Equal((ushort)0, code);
    }

    // ---- Run control ----

    [Fact]
    public void Run_OnHaltedMachine_IsNoOp()
    {
        var vm = new TestVm().Load(0x2400);
        vm.Machine.Run();
        var ic = vm.Machine.InstructionCount;
        vm.Machine.Run();
        Assert.Equal(ic, vm.Machine.InstructionCount);
        Assert.Equal(MachineState.Halted, vm.Machine.State);
    }

    [Fact]
    public void Run_OnFaultedMachine_IsNoOp()
    {
        var vm = new TestVm().Load(0x2100);
        vm.Machine.Run();
        var ic = vm.Machine.InstructionCount;
        vm.Machine.Run();
        Assert.Equal(ic, vm.Machine.InstructionCount);
        Assert.Equal(MachineState.Faulted, vm.Machine.State);
    }

    // ---- Load / Reset ----

    [Fact]
    public void Load_OddByteLength_Throws()
    {
        var vm = new TestVm();
        Assert.Throws<ArgumentException>(() => vm.Machine.Load(new byte[] { 0x00, 0x24, 0x01 }));
    }

    [Fact]
    public void Load_ImageLargerThanRam_Throws()
    {
        var vm = new TestVm();
        var words = new ushort[Memory.RamSize + 1];
        Assert.Throws<ArgumentException>(() => vm.Machine.Load(words));
        var bytes = new byte[2 * (Memory.RamSize + 1)];
        Assert.Throws<ArgumentException>(() => vm.Machine.Load(bytes));
    }

    [Fact]
    public void Load_LittleEndianByteOrder()
    {
        var vm = new TestVm();
        // 0x2400 HALT as little-endian bytes: 00 24.
        vm.Machine.Load(new byte[] { 0x00, 0x24 });
        Assert.Equal((ushort)0x2400, vm.Machine.ReadMemory(0x0000));
        vm.Machine.Run();
        vm.ExpectHalted();
    }

    [Fact]
    public void SecondLoad_FullyResets()
    {
        var vm = new TestVm().Load(0x1B05, 0x2400);
        vm.Machine.Run();
        Assert.Equal((ushort)5, vm.Top);

        var before = vm.Machine.ReadMemory(0x0100);
        vm.Machine.WriteMemory(0x0100, 0xDEAD);
        vm.Load(0x1B07, 0x2400);
        Assert.Equal(MachineState.Paused, vm.Machine.State);
        Assert.Equal(0, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0x0000, vm.Machine.Pc);
        Assert.Equal(FaultCode.None, vm.Machine.LastFault);
        Assert.Equal(0, vm.OperandCount);
        Assert.Equal((ushort)before, vm.Machine.ReadMemory(0x0100)); // RAM zero-filled
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)7, vm.Top);
    }

    [Fact]
    public void Reset_ClearedState_BreakpointsAndWatchpointsSurvive()
    {
        var vm = new TestVm().Load(0x1B05, 0x2400);
        vm.Machine.SetBreakpoint(0x0001);
        vm.Machine.SetWatchpoint(0x0002);
        vm.Machine.Run();
        vm.Machine.Reset();
        Assert.Equal(MachineState.Paused, vm.Machine.State);
        Assert.Equal((ushort)0x0000, vm.Machine.Pc);
        Assert.Equal(0, vm.Machine.InstructionCount);
        Assert.Equal(0, vm.OperandCount);
        Assert.Equal(0, vm.Machine.ReadMemory(0x0000)); // RAM zeroed
        Assert.True(vm.Machine.HasBreakpoint(0x0001));
        Assert.True(vm.Machine.HasWatchpoint(0x0002));
    }

    // ---- Memory debug API ----

    [Fact]
    public void ReadMemory_PortAddress_Throws()
    {
        var vm = new TestVm();
        Assert.Throws<ArgumentException>(() => vm.Machine.ReadMemory(0xF000));
    }

    [Fact]
    public void WriteMemory_PortAddress_Throws()
    {
        var vm = new TestVm();
        Assert.Throws<ArgumentException>(() => vm.Machine.WriteMemory(0xF000, 0));
    }

    [Fact]
    public void MemoryWindow_ReturnsContiguousWords()
    {
        var vm = new TestVm();
        vm.Machine.WriteMemory(0x000A, 0x1111);
        vm.Machine.WriteMemory(0x000B, 0x2222);
        var window = vm.Machine.MemoryWindow(0x000A, 3);
        Assert.Equal(new ushort[] { 0x1111, 0x2222, 0x0000 }, window);
    }
}
