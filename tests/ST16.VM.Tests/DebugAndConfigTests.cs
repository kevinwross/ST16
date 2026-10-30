namespace ST16.VM.Tests;

/// <summary>Debug hooks (breakpoints, stepping, limit, snapshots, determinism) + configuration.</summary>
public class DebugAndConfigTests
{
    [Fact]
    public void Breakpoint_PausesBeforeExecuting()
    {
        var m = TestKit.Machine(new[] { TestKit.Encode(Op.Push, 1), TestKit.Encode(Op.Halt) });
        m.SetBreakpoint(1);
        Assert.True(m.HasBreakpoint(1));

        m.Step();                                 // PUSH executes
        Assert.Equal((ushort)1, m.PC);
        Assert.Equal(MachineState.Running, m.State);

        var events = 0;
        m.BreakpointHit += _ => events++;
        m.Step();                                 // breakpoint at HALT's PC
        Assert.Equal(MachineState.Paused, m.State);
        Assert.Equal(StopReason.Breakpoint, m.StopReason);
        Assert.Equal((ushort)1, m.PC);
        Assert.Equal(1, events);
        Assert.Equal(1u, m.InstructionCount);     // HALT did NOT execute

        m.ClearBreakpoint(1);
        m.Resume();
        m.Step();
        Assert.Equal(MachineState.Halted, m.State);
        Assert.Equal(2u, m.InstructionCount);
    }

    [Fact]
    public void Run_StopsAtBreakpoint()
    {
        var m = TestKit.Machine(new[] { TestKit.Encode(Op.Push, 1), TestKit.Encode(Op.Push, 2), TestKit.Encode(Op.Halt) });
        m.SetBreakpoint(2);
        m.Run();
        Assert.Equal(MachineState.Paused, m.State);
        Assert.Equal((ushort)2, m.PC);
    }

    [Fact]
    public void InstructionLimit_PausesWithLimitReached()
    {
        var cfg = new ST16Config { InstructionLimit = 3 };
        // infinite loop: 0000: JMP 0
        var m = new ST16Machine(cfg);
        m.LoadProgram(new ushort[] { TestKit.Encode(Op.Jmp), 0x0000 });
        m.Run();
        Assert.Equal(MachineState.Paused, m.State);
        Assert.Equal(StopReason.LimitReached, m.StopReason);
        Assert.Equal(3u, m.InstructionCount);

        m.Resume();
        m.Step();
        Assert.Equal(MachineState.Paused, m.State);     // limit still enforced
        Assert.Equal(4u, m.InstructionCount);
    }

    [Fact]
    public void InstructionCompleted_EventsCarryIndexPcOpcode()
    {
        var m = TestKit.Machine(new[] { TestKit.Encode(Op.Push, 5), TestKit.Encode(Op.Halt) });
        var seen = new List<InstructionCompletedEventArgs>();
        m.InstructionCompleted += e => seen.Add(e);
        m.Run();

        Assert.Equal(2, seen.Count);
        Assert.Equal(0u, seen[0].Index);
        Assert.Equal((ushort)0, seen[0].PC);
        Assert.Equal(Op.Push, seen[0].Opcode);
        Assert.Equal((ushort)5, seen[0].Operand);
        Assert.Equal(TestKit.Encode(Op.Push, 5), seen[0].Word);
        Assert.Equal(1u, seen[1].Index);
        Assert.Equal((ushort)1, seen[1].PC);
        Assert.Equal(Op.Halt, seen[1].Opcode);
    }

    [Fact]
    public void FaultAndHalt_EventsFire()
    {
        var m1 = TestKit.Machine(new[] { TestKit.Encode(Op.Div) }, 1, 0);
        var m2 = TestKit.Machine(new[] { TestKit.Encode(Op.Halt) });

        int faults = 0, halts = 0;
        m1.FaultOccurred += e => { faults++; Assert.Equal(ST16Machine.FaultDiv0, e.Code); };
        m1.Step();                                   // subscribe before stepping
        m2.Halted += e => { halts++; Assert.Equal((ushort)0, e.PC); };
        m2.Run();
        Assert.Equal(1, faults);
        Assert.Equal(1, halts);
    }

    [Fact]
    public void StepOver_CallsIntoAndOutOfSubroutine()
    {
        // 0000: CALL 0x0003 ; 0002: HALT ; 0003: PUSH 5 ; 0004: RET ; 0005: HALT
        var m = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Call), 0x0003,
            TestKit.Encode(Op.Halt),
            TestKit.Encode(Op.Push, 5), TestKit.Encode(Op.Ret),
            TestKit.Encode(Op.Halt),
        });
        m.StepOver();
        Assert.Equal((ushort)2, m.PC);               // back after the CALL
        Assert.Empty(m.CallStack);
        TestKit.AssertStack(m, 5);
        m.Step();
        Assert.Equal(MachineState.Halted, m.State);
    }

    [Fact]
    public void StepOut_ReturnsToCaller()
    {
        // caller at 0 (CALL 0x0003), callee body at 0x0003: PUSH 5, RET
        var m = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Call), 0x0003, TestKit.Encode(Op.Halt),
            TestKit.Encode(Op.Push, 5), TestKit.Encode(Op.Ret),
            TestKit.Encode(Op.Halt),
        });
        m.Step();                                     // CALL -> PC 3, frame pushed
        m.Step();                                     // PUSH 5 -> PC 4
        m.StepOut();                                  // RET -> PC 2
        Assert.Equal((ushort)2, m.PC);
        Assert.Empty(m.CallStack);
        TestKit.AssertStack(m, 5);
    }

    [Fact]
    public void StepOut_NoFrame_StepsOne()
    {
        var m = TestKit.Machine(new[] { TestKit.Encode(Op.Push, 1), TestKit.Encode(Op.Halt) });
        m.StepOut();
        Assert.Equal((ushort)1, m.PC);
        Assert.Equal(1u, m.InstructionCount);
    }

    [Fact]
    public void Snapshot_Restore_RerunsIdentically()
    {
        var out1 = new InMemoryOutput();
        var m = new ST16Machine(output: out1);
        Assert.True(m.LoadProgram(ControlFlowTests.Starloop));
        for (int i = 0; i < 20; i++) m.Step();          // mid-run (2 asterisks printed)
        var snap = m.TakeSnapshot();
        uint countAtSnap = m.InstructionCount;

        m.Run();
        Assert.Equal("**********", out1.AsText());
        Assert.Equal(MachineState.Halted, m.State);

        // A fresh machine restoring the snapshot runs the remainder identically.
        var out2 = new InMemoryOutput();
        var m2 = new ST16Machine(output: out2);
        Assert.True(m2.LoadProgram(ControlFlowTests.Starloop));
        m2.Restore(snap);
        Assert.Equal(countAtSnap, m2.InstructionCount);
        m2.Run();
        Assert.Equal(8, out2.AsText().Count(c => c == '*'));  // remaining asterisks
        Assert.Equal(MachineState.Halted, m2.State);
        Assert.Equal(m.InstructionCount, m2.InstructionCount);
    }

    [Fact]
    public void Determinism_SameProgramSameInput_SameResult()
    {
        ushort[] Program() => new ushort[]
        {
            TestKit.Encode(Op.Push16), 0xF001, TestKit.Encode(Op.Load),
            TestKit.Encode(Op.Push16), 0xF000, TestKit.Encode(Op.Store),
            TestKit.Encode(Op.Halt),
        };
        (string text, uint count, MachineState state) Run()
        {
            var outp = new InMemoryOutput();
            var mm = new ST16Machine(input: new InMemoryInput("A"), output: outp);
            mm.LoadProgram(Program());
            mm.Run();
            return (outp.AsText(), mm.InstructionCount, mm.State);
        }
        var (t1, c1, s1) = Run();
        var (t2, c2, s2) = Run();
        Assert.Equal(t1, t2);
        Assert.Equal(c1, c2);
        Assert.Equal(s1, s2);
        Assert.Equal("A", t1);
        Assert.Equal(MachineState.Halted, s1);
    }
}

/// <summary>Configuration validation and machine-model edges.</summary>
public class ConfigTests
{
    [Fact] public void Depths_BelowFour_Throw() => Assert.Throws<ArgumentOutOfRangeException>(() => new ST16Config { OperandStackDepth = 3 });
    [Fact] public void Depths_Above256_Throw() => Assert.Throws<ArgumentOutOfRangeException>(() => new ST16Config { CallStackDepth = 257 });
    [Fact] public void Depths_Boundaries_Accepted()
    {
        Assert.Equal(4, new ST16Config { OperandStackDepth = 4 }.OperandStackDepth);
        Assert.Equal(256, new ST16Config { CallStackDepth = 256 }.CallStackDepth);
    }

    [Fact]
    public void CallDepth_4_FifthNestedCall_Faults()
    {
        var cfg = new ST16Config { CallStackDepth = 4 };
        // loop that calls itself: 0000: CALL 0 ; 0002: RET
        var m = new ST16Machine(cfg);
        m.LoadProgram(new ushort[] { TestKit.Encode(Op.Call), 0x0000, TestKit.Encode(Op.Ret) });
        m.Run();
        Assert.Equal(MachineState.Faulted, m.State);
        Assert.Equal(ST16Machine.FaultCallOf, m.FaultCode);
    }

    [Fact]
    public void SameProgram_Depth32_NoFault()
    {
        var m = new ST16Machine();
        m.LoadProgram(new ushort[] { TestKit.Encode(Op.Call), 0x0000, TestKit.Encode(Op.Ret) });
        m.Run();
        Assert.Equal(ST16Machine.FaultCallOf, m.FaultCode);   // still faults eventually (infinite loop)
        Assert.Equal(MachineState.Faulted, m.State);
    }

    [Fact]
    public void Reset_RestoresInitialState()
    {
        var m = TestKit.Machine(new[] { TestKit.Encode(Op.Push, 1), TestKit.Encode(Op.Halt) });
        m.WriteMemory(0x100, 0xABCD);
        m.SetBreakpoint(1);
        m.Step();
        m.Reset();
        Assert.Equal(MachineState.Running, m.State);
        Assert.Equal(StopReason.None, m.StopReason);
        Assert.Equal((ushort)0, m.PC);
        Assert.Equal(0u, m.InstructionCount);
        Assert.Empty(m.OperandStack);
        Assert.Equal(0, m.ReadMemory(0x100));
        Assert.False(m.HasBreakpoint(1));
    }

    [Fact]
    public void LoadProgram_RejectsOverflowBeyondRam()
    {
        var m = new ST16Machine();
        Assert.False(m.LoadProgram(new ushort[0x10], 0xEFF1));   // last word would land at 0xF000
        Assert.True(m.LoadProgram(new ushort[0x10], 0xEEF0));    // fits exactly (last word 0xEFFF)
    }

    [Fact]
    public void ReadMemory_Ports_Throw()
    {
        var m = new ST16Machine();
        Assert.Throws<ArgumentOutOfRangeException>(() => m.ReadMemory(0xF000));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.WriteMemory(0xFFFF, 1));
    }

    [Fact]
    public void ReservedOpcodes_25Through3F_AllIllop()
    {
        for (ushort op = 0x25; op <= 0x3F; op++)
        {
            var m = TestKit.Step1(op, 0);
            Assert.True(m.FaultCode == ST16Machine.FaultIllop, $"opcode 0x{op:X2}");
        }
    }
}
