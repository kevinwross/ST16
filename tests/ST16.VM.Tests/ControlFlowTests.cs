namespace ST16.VM.Tests;

/// <summary>Spec §5.5 control flow + the §9.3 starloop end-to-end example.</summary>
public class ControlFlowTests
{
    /// <summary>The exact 25-word starloop program from spec §9.3 (JMP header = 0x1F00;
    /// counter initialized to 9 per spec erratum §9.3 so the example prints exactly 10 asterisks).</summary>
    public static readonly ushort[] Starloop =
    {
        0x1B09, 0x1C00, 0x0018, 0x1E00,   // PUSH 9; PUSH16 cnt; STORE
        0x1C00, 0x002A, 0x0000, 0x1C00,   // PUSH16 '*'; DUP; PUSH16
        0xF000, 0x1E00, 0x1C00, 0x0018,   // 0xF000; STORE; PUSH16 cnt
        0x1D00, 0x0000, 0x2207, 0x1B01,   // LOAD; DUP; JZ +7; PUSH 1
        0x0600, 0x1C00, 0x0018, 0x1E00,   // SUB; PUSH16 cnt; STORE
        0x1F00, 0x0006, 0x0400, 0x2400,   // JMP loop; DROP (done); HALT
        0x0000,                           // .dw 0
    };

    [Fact]
    public void Starloop_PrintsTenAsterisks()
    {
        var output = new InMemoryOutput();
        var m = new ST16Machine(output: output);
        Assert.True(m.LoadProgram(Starloop));
        m.Run();

        Assert.Equal("**********", output.AsText());
        Assert.Equal(MachineState.Halted, m.State);
        Assert.Equal(StopReason.Halted, m.StopReason);
        Assert.Equal(0, m.FaultCode);
        Assert.True(m.InstructionCount > 0);
    }

    [Fact]
    public void Jmp_TwoWordAbsolute()
    {
        // 0000: JMP 0x0004 ; 0002: PUSH 1 ; 0004: PUSH 2 ; 0005: HALT
        var m = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Jmp), 0x0004,
            TestKit.Encode(Op.Push, 1), TestKit.Encode(Op.Halt),
            TestKit.Encode(Op.Push, 2), TestKit.Encode(Op.Halt),
        });
        m.Step();
        Assert.Equal((ushort)4, m.PC);
        m.Step(); m.Step();
        Assert.Equal(MachineState.Halted, m.State);
        TestKit.AssertStack(m, 2);
    }

    [Fact]
    public void Jz_TakenAndNotTaken()
    {
        // taken: [0] JZ +1 -> skips PUSH 9
        var m = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Jz, 1), TestKit.Encode(Op.Push, 9), TestKit.Encode(Op.Halt),
        }, 0);
        m.Step();
        Assert.Equal((ushort)2, m.PC);     // jumped over the PUSH

        // not taken: [5] JZ +1 -> executes PUSH 9
        var m2 = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Jz, 1), TestKit.Encode(Op.Push, 9), TestKit.Encode(Op.Halt),
        }, 5);
        m2.Step();
        Assert.Equal((ushort)1, m2.PC);
        m2.Step();
        TestKit.AssertStack(m2, 9);
    }

    [Fact]
    public void Jnz_TakenAndNotTaken()
    {
        var m = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Jnz, 1), TestKit.Encode(Op.Push, 9), TestKit.Encode(Op.Halt),
        }, 7);
        m.Step();
        Assert.Equal((ushort)2, m.PC);     // taken

        var m0 = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Jnz, 1), TestKit.Encode(Op.Push, 9), TestKit.Encode(Op.Halt),
        }, 0);
        m0.Step();
        Assert.Equal((ushort)1, m0.PC);    // not taken
    }

    [Fact]
    public void Jz_NegativeOffsetCountsBack()
    {
        // 0000: JZ -1 (0xFF) -> target (0+1-1)=0 : infinite loop guarded by limit
        var m = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Jz, 0xFF),
        }, 0);
        m.Step();
        Assert.Equal((ushort)0, m.PC);     // wrapped back to itself
    }

    [Fact]
    public void Jz_NegativeOffsetWrapsPcAcross0x0000()
    {
        // JZ -2 at PC 0, taken: (0 + 1) + (-2) = -1 -> wraps to 0xFFFF.
        // (A positive JZ offset can never wrap past 0xFFFF from RAM: max (pc+1) is 0xF000
        // and the offset is at most +127, so the PC arithmetic here is the wrap case.)
        var m = TestKit.Machine(new ushort[] { TestKit.Encode(Op.Jz, 0xFE), TestKit.Encode(Op.Halt) }, 0);
        m.Step();
        Assert.Equal((ushort)0xFFFF, m.PC);
    }

    [Fact]
    public void Call_PushesPcPlus2_Ret_Returns()
    {
        // 0000: CALL 0x0003 ; 0002: HALT ; 0003: PUSH 42 ; 0004: RET ; 0005: HALT
        var m = TestKit.Machine(new ushort[]
        {
            TestKit.Encode(Op.Call), 0x0003,
            TestKit.Encode(Op.Halt),
            TestKit.Encode(Op.Push, 42), TestKit.Encode(Op.Ret),
            TestKit.Encode(Op.Halt),
        });
        m.Step();                                   // CALL
        Assert.Equal((ushort)3, m.PC);
        Assert.Single(m.CallStack);
        Assert.Equal((ushort)2, m.CallStack[0]);    // return address = PC+2
        m.Step(); m.Step();                         // PUSH 42; RET
        Assert.Equal((ushort)2, m.PC);
        Assert.Empty(m.CallStack);
        m.Step();                                   // HALT
        Assert.Equal(MachineState.Halted, m.State);
        TestKit.AssertStack(m, 42);
    }

    [Fact]
    public void Ret_EmptyCallStack_FaultsCallUf()
    {
        var m = TestKit.Step1(Op.Ret, 0);
        Assert.Equal(MachineState.Faulted, m.State);
        Assert.Equal(ST16Machine.FaultCallUf, m.FaultCode);
    }

    [Fact]
    public void Halt_TerminalWithStopReason()
    {
        var m = TestKit.Machine(new[] { TestKit.Encode(Op.Halt) });
        m.Run();
        Assert.Equal(MachineState.Halted, m.State);
        Assert.Equal(StopReason.Halted, m.StopReason);
        Assert.False(m.Step());                     // nothing more runs
    }
}
