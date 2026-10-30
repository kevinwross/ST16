using ST16.VM;
using Xunit;

namespace ST16.VM.Tests;

/// <summary>
/// Full fault matrix (spec §6): all 7 fault codes, the fault-priority rule
/// (ILLOP → STACKUF → DIV0/ADDRF → STACKOF), and atomicity — a faulting
/// instruction has no partial effects (all fault conditions are checked
/// before any machine state is mutated).
/// </summary>
public class FaultTests
{
    /// <summary>Asserts atomicity: the faulting instruction left no partial effects.</summary>
    private static void AssertAtomic(MachineSnapshot before, ST16Machine m)
    {
        var after = m.TakeSnapshot();
        Assert.Equal(before.Pc, m.Pc);
        Assert.Equal(before.InstructionCount, after.InstructionCount);
        Assert.Equal(before.OperandStackTopDown, after.OperandStackTopDown);
        Assert.Equal(before.OperandCount, after.OperandCount);
        Assert.Equal(before.CallStackTopDown, after.CallStackTopDown);
        Assert.Equal(before.CallCount, after.CallCount);
    }

    // ---- DIV0 (code 1) ----

    [Fact]
    public void Div0_DivByZero_Atomic()
    {
        var vm = new TestVm().Load(0x1B05, 0x1B00, 0x0900, 0x2400); // PUSH 5, PUSH 0, DIV, HALT
        var before = vm.Machine.TakeSnapshot();
        // Step to the DIV (2 instructions in), then snapshot the pre-fault state.
        vm.Machine.StepInto();
        vm.Machine.StepInto();
        before = vm.Machine.TakeSnapshot();
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.Div0, 0x0002);
        Assert.Equal(2, vm.Machine.InstructionCount);
        AssertAtomic(before, vm.Machine);
        Assert.Equal(new ushort[] { 0, 5 }, vm.StackTopDown()); // stack intact
    }

    [Fact]
    public void Div0_ModByZero()
    {
        var vm = new TestVm().Load(0x1B05, 0x1B00, 0x0A00, 0x2400);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.Div0, 0x0002);
        Assert.Equal(2, vm.Machine.InstructionCount);
    }

    // ---- STACKUF (code 2) ----

    [Fact]
    public void StackUf_DupOnEmptyStack()
    {
        var vm = new TestVm().Load(0x0000, 0x2400);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.StackUf, 0x0000);
        Assert.Equal(0, vm.Machine.InstructionCount);
    }

    [Theory]
    [InlineData(0x0000, 0)]       // DUP empty
    [InlineData(0x0500, 1)]       // ADD with one element (preloaded below)
    [InlineData(0x0600, 1)]       // SUB with one element
    [InlineData(0x1100, 1)]       // EQ with one element
    [InlineData(0x1D00, 0)]       // LOAD with empty stack
    [InlineData(0x2201, 0)]       // JZ with empty stack
    [InlineData(0x2301, 0)]       // JNZ with empty stack
    public void StackUf_OneOperandInstructions(ushort instr, int preloaded)
    {
        var vm = new TestVm();
        var words = new List<ushort>();
        for (int i = 0; i < preloaded; i++)
            words.Add(0x1B05); // PUSH 5
        words.Add(instr);
        words.Add(0x2400);
        vm.Load(words.ToArray());
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.StackUf, (ushort)preloaded);
        Assert.Equal(preloaded, vm.Machine.InstructionCount);
    }

    [Fact]
    public void StackUf_RotWithTwoElements()
    {
        var vm = new TestVm().Load(0x1B01, 0x1B02, 0x0300, 0x2400);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.StackUf, 0x0002);
        Assert.Equal(2, vm.Machine.InstructionCount);
        Assert.Equal(new ushort[] { 2, 1 }, vm.StackTopDown()); // intact
    }

    [Fact]
    public void StackUf_StoreWithOneElement_Atomic()
    {
        var vm = new TestVm().Load(0x1B09, 0x1E00, 0x2400); // PUSH 9, STORE (needs 2), HALT
        var before = vm.Machine.TakeSnapshot();
        vm.Machine.StepInto();
        before = vm.Machine.TakeSnapshot();
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.StackUf, 0x0001);
        Assert.Equal(1, vm.Machine.InstructionCount);
        AssertAtomic(before, vm.Machine);
    }

    // ---- STACKOF (code 3) ----

    [Fact]
    public void StackOf_DefaultDepth32_Push33()
    {
        var words = Enumerable.Repeat((ushort)0x1B01, 33).ToArray();
        var vm = new TestVm().Load(words);
        for (int i = 0; i < 32; i++)
            vm.Machine.StepInto();
        var before = vm.Machine.TakeSnapshot();
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.StackOf, 32);
        Assert.Equal(32, vm.Machine.InstructionCount);
        AssertAtomic(before, vm.Machine);
        Assert.Equal(32, vm.OperandCount); // 32 intact values
    }

    [Fact]
    public void StackOf_StrictDepth4_EachPushingInstruction()
    {
        const ushort Push1 = 0x1B01;

        // 5th PUSH → STACKOF.
        var push = new TestVm(new St16Config(4, 4)).Load(Push1, Push1, Push1, Push1, Push1, 0x2400);
        push.Machine.Run();
        push.ExpectFault(FaultCode.StackOf, 4);
        Assert.Equal(4, push.Machine.InstructionCount);
        Assert.Equal(4, push.OperandCount);

        // DUP on a full stack → STACKOF.
        var dup = new TestVm(new St16Config(4, 4)).Load(Push1, Push1, Push1, Push1, 0x0000, 0x2400);
        dup.Machine.Run();
        dup.ExpectFault(FaultCode.StackOf, 4);
        Assert.Equal(4, dup.OperandCount);

        // OVER on a full stack → STACKOF.
        var over = new TestVm(new St16Config(4, 4)).Load(Push1, Push1, Push1, Push1, 0x0200, 0x2400);
        over.Machine.Run();
        over.ExpectFault(FaultCode.StackOf, 4);
        Assert.Equal(4, over.OperandCount);

        // PUSH16 on a full stack → STACKOF.
        var push16 = new TestVm(new St16Config(4, 4)).Load(Push1, Push1, Push1, Push1, 0x1C00, 0x0100, 0x2400);
        push16.Machine.Run();
        push16.ExpectFault(FaultCode.StackOf, 4);
        Assert.Equal(4, push16.OperandCount);

        // LOAD on a full stack (RAM address, so no ADDRF) → STACKOF. Three pushes
        // + the PUSH16 address word fill the depth-4 stack; the LOAD faults.
        var load = new TestVm(new St16Config(4, 4)).Load(Push1, Push1, Push1, 0x1C00, 0x0100, 0x1D00, 0x2400);
        load.Machine.Run();
        // LOAD at 0x0005 (PUSH16 occupies 0x0003–0x0004).
        load.ExpectFault(FaultCode.StackOf, 5);
        Assert.Equal(4, load.OperandCount);
    }

    // ---- ILLOP (code 4) ----

    [Theory]
    [InlineData(0x2500)] // first reserved opcode
    [InlineData(0x3F00)] // last reserved opcode
    [InlineData(0x4000)] // bit 14 set
    [InlineData(0x8000)] // bit 15 set
    [InlineData(0xC000)] // both bits set
    [InlineData(0xFFFF)] // both bits + reserved opcode
    public void Illop_ReservedOrBitGuard(ushort word)
    {
        var vm = new TestVm().Load(word);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.Illop, 0x0000);
        Assert.Equal(0, vm.Machine.InstructionCount);
    }

    [Fact]
    public void FaultPriority_IllopBeatsStackUf()
    {
        // Reserved opcode on an empty stack: ILLOP (decode) wins over STACKUF.
        var vm = new TestVm().Load(0x2500);
        vm.Machine.Run();
        Assert.Equal(FaultCode.Illop, vm.Machine.LastFault);
    }

    [Fact]
    public void FaultPriority_StackUfBeatsDiv0()
    {
        // DIV with an empty stack: underflow is checked before the DIV0 condition.
        var vm = new TestVm().Load(0x0900);
        vm.Machine.Run();
        Assert.Equal(FaultCode.StackUf, vm.Machine.LastFault);
    }

    [Fact]
    public void FaultPriority_AddrfBeatsStackOf()
    {
        // LOAD from an unmapped port on a full stack: ADDRF wins over STACKOF
        // (spec §6 priority: DIV0/ADDRF before STACKOF). Three pushes + the
        // PUSH16 address word fill the depth-4 stack; the LOAD then sees an
        // unmapped port while full.
        var vm = new TestVm(new St16Config(4, 4))
            .Load(0x1B01, 0x1B01, 0x1B01, 0x1C00, 0xF003, 0x1D00, 0x2400);
        vm.Machine.Run();
        // LOAD at 0x0005 (PUSH16 occupies 0x0003–0x0004).
        vm.ExpectFault(FaultCode.AddrF, 5);
    }

    // ---- ADDRF (code 5) ----

    [Fact]
    public void AddrF_LoadUnmappedPort_Atomic()
    {
        var vm = new TestVm().Load(0x1C00, 0xF003, 0x1D00, 0x2400);
        vm.Machine.StepInto(); // PUSH16 0xF003 (two words)
        var before = vm.Machine.TakeSnapshot();
        vm.Machine.Run();      // LOAD → ADDRF
        vm.ExpectFault(FaultCode.AddrF, 0x0002);
        Assert.Equal(1, vm.Machine.InstructionCount);
        AssertAtomic(before, vm.Machine);
        Assert.Equal(new ushort[] { 0xF003 }, vm.StackTopDown()); // address still on stack
    }

    [Fact]
    public void AddrF_Load0xFFFF()
    {
        var vm = new TestVm().Load(0x1C00, 0xFFFF, 0x1D00, 0x2400);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.AddrF, 0x0002);
    }

    [Fact]
    public void AddrF_StoreUnmappedPort_Atomic()
    {
        var vm = new TestVm().Load(0x1B09, 0x1C00, 0xF004, 0x1E00, 0x2400);
        vm.Machine.StepInto(); // PUSH 9
        vm.Machine.StepInto(); // PUSH16 0xF004 (two words)
        var before = vm.Machine.TakeSnapshot();
        vm.Machine.Run();      // STORE → ADDRF
        vm.ExpectFault(FaultCode.AddrF, 0x0003);
        Assert.Equal(2, vm.Machine.InstructionCount);
        AssertAtomic(before, vm.Machine);
        Assert.Equal(new ushort[] { 0xF004, 9 }, vm.StackTopDown()); // nothing popped
    }

    [Fact]
    public void AddrF_Store0xFFFF()
    {
        var vm = new TestVm().Load(0x1B09, 0x1C00, 0xFFFF, 0x1E00, 0x2400);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.AddrF, 0x0003);
    }

    [Fact]
    public void AddrF_InstructionFetchFromPortRegion_Jmp()
    {
        // Spec §3 erratum: a fetch from the port region is ADDRF; the fault PC is
        // the port address the machine tried to fetch.
        var vm = new TestVm().Load(0x1F00, 0xF000); // JMP 0xF000
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.AddrF, 0xF000);
        Assert.Equal(1, vm.Machine.InstructionCount); // the JMP itself completed
    }

    [Fact]
    public void AddrF_InstructionFetchFromPortRegion_Ret()
    {
        // CALL a port-region address, then RET into it: the RET completes, the
        // following fetch faults.
        // 0x0000 CALL 0x0002; 0x0002 CALL 0xF001; (RET at 0x0004 is never reached).
        var vm = new TestVm().Load(0x2000, 0x0002, 0x2000, 0xF001, 0x2100);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.AddrF, 0xF001);
        Assert.Equal(2, vm.Machine.InstructionCount);
        Assert.Equal(2, vm.Machine.CallDepth); // both CALLs completed
    }

    [Fact]
    public void AddrF_TwoWordInstructionStraddlingRamPortBoundary()
    {
        // PUSH16 header at 0xEFFF: its second word would sit at 0xF000 (port region)
        // → ADDRF at the header PC (spec §4 two-word rule + §3 fetch rule).
        var vm = new TestVm();
        vm.Machine.Reset();
        vm.Machine.WriteMemory(0x0000, 0x1F00); // JMP 0xEFFF
        vm.Machine.WriteMemory(0x0001, 0xEFFF);
        vm.Machine.WriteMemory(0xEFFF, 0x1C00);  // PUSH16 header
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.AddrF, 0xEFFF);
        Assert.Equal(1, vm.Machine.InstructionCount);
    }

    // ---- CALLOF (code 6) ----

    [Fact]
    public void CallOf_DefaultDepth32_ThirtyThirdSelfCall()
    {
        var vm = new TestVm().Load(0x2000, 0x0000); // CALL 0x0000 (self)
        for (int i = 0; i < 32; i++)
            vm.Machine.StepInto();
        var before = vm.Machine.TakeSnapshot();
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.CallOf, 0x0000);
        Assert.Equal(32, vm.Machine.InstructionCount);
        Assert.Equal(32, vm.Machine.CallDepth); // all 32 frames intact
        AssertAtomic(before, vm.Machine);
        // Every frame holds PC+2 of a self-CALL at 0x0000 = 0x0002.
        Assert.All(vm.Machine.CallStack, frame => Assert.Equal((ushort)0x0002, frame));
    }

    [Fact]
    public void CallOf_StrictDepth4_FifthSelfCall()
    {
        var vm = new TestVm(new St16Config(32, 4)).Load(0x2000, 0x0000);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.CallOf, 0x0000);
        Assert.Equal(4, vm.Machine.InstructionCount);
        Assert.Equal(4, vm.Machine.CallDepth);
    }

    // ---- CALLUF (code 7) ----

    [Fact]
    public void CallUf_RetWithEmptyCallStack()
    {
        var vm = new TestVm().Load(0x2100, 0x2400); // RET, HALT
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.CallUf, 0x0000);
        Assert.Equal(0, vm.Machine.InstructionCount);
    }
}
