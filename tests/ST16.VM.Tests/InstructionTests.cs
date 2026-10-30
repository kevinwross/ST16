using ST16.VM;
using Xunit;

namespace ST16.VM.Tests;

/// <summary>
/// All 37 v1.0 instructions (spec §5) — normal operation and edge cases.
/// Stack notation below is top-first (s1 = top).
/// </summary>
public class InstructionTests
{
    // ---- Stack manipulation (0x00–0x04) ----

    [Fact]
    public void Dup_DuplicatesTop()
    {
        var vm = new TestVm().Load(0x1B05, 0x1B07, 0x0000, 0x2400); // PUSH 5, PUSH 7, DUP, HALT
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new ushort[] { 7, 7, 5 }, vm.StackTopDown());
    }

    [Fact]
    public void Swap_ExchangesTopTwo()
    {
        var vm = new TestVm().Load(0x1B05, 0x1B07, 0x0100, 0x2400); // PUSH 5, PUSH 7, SWAP, HALT
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new ushort[] { 5, 7 }, vm.StackTopDown());
    }

    [Fact]
    public void Over_CopiesSecondOntoTop()
    {
        // a b → a b a (b = top): PUSH 5, PUSH 7, OVER → top-first [5, 7, 5].
        var vm = new TestVm().Load(0x1B05, 0x1B07, 0x0200, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new ushort[] { 5, 7, 5 }, vm.StackTopDown());
    }

    [Fact]
    public void Rot_MovesBottomOfThreeToTop()
    {
        // PUSH 10, PUSH 20, PUSH 30, ROT, HALT. a b c → b c a (c = top).
        var vm = new TestVm().Load(0x1B0A, 0x1B14, 0x1B1E, 0x0300, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new ushort[] { 10, 30, 20 }, vm.StackTopDown());
    }

    [Fact]
    public void Drop_PopsTop()
    {
        var vm = new TestVm().Load(0x1B05, 0x1B07, 0x0400, 0x2400); // PUSH 5, PUSH 7, DROP, HALT
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new ushort[] { 5 }, vm.StackTopDown());
    }

    // ---- Arithmetic (0x05–0x10) ----

    [Fact]
    public void Add_WrapsModulo216()
    {
        var vm = new TestVm().Load(0x1BFE, 0x1B03, 0x0500, 0x2400); // -2 + 3
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)1, vm.Top);

        var wrap = new TestVm().Load(0x1BFE, 0x1B02, 0x0500, 0x2400); // -2 + 2 = 0
        wrap.Machine.Run();
        wrap.ExpectHalted();
        Assert.Equal((ushort)0, wrap.Top);
    }

    [Fact]
    public void Sub_TopIsSubtrahend()
    {
        // Spec §5.2: SUB: a b → a−b with b (top) the subtrahend.
        var vm = new TestVm().Load(0x1B05, 0x1B03, 0x0600, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)2, vm.Top); // 5 − 3
    }

    [Fact]
    public void Neg_Wraps0x8000ToItself()
    {
        var vm = new TestVm().Load(0x1C00, 0x8000, 0x0700, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0x8000, vm.Top);
    }

    [Fact]
    public void Neg_NormalValue()
    {
        var vm = new TestVm().Load(0x1BF6, 0x0700, 0x2400); // PUSH −10, NEG → 10
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)10, vm.Top);
    }

    [Fact]
    public void Mul_TakesLow16Bits()
    {
        var vm = new TestVm().Load(0x1BFF, 0x1B02, 0x0800, 0x2400); // -1 × 2 = 0xFFFE
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0xFFFE, vm.Top);
    }

    [Fact]
    public void Div_TruncatesTowardZero()
    {
        // −7 ÷ 2 = −3 (not −4 as floor division would give).
        var vm = new TestVm().Load(0x1BF9, 0x1B02, 0x0900, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0xFFFD, vm.Top);
    }

    [Fact]
    public void Div_ByNegativeTruncatesTowardZero()
    {
        var vm = new TestVm().Load(0x1B07, 0x1BFE, 0x0900, 0x2400); // 7 ÷ −2 = −3
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0xFFFD, vm.Top);
    }

    [Fact]
    public void Div_0x8000ByMinus1WrapsWithoutFault()
    {
        var vm = new TestVm().Load(0x1C00, 0x8000, 0x1BFF, 0x0900, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0x8000, vm.Top);
    }

    [Fact]
    public void Mod_SignFollowsDividendC99()
    {
        // 7 mod −2 = 1
        var a = new TestVm().Load(0x1B07, 0x1BFE, 0x0A00, 0x2400);
        a.Machine.Run();
        a.ExpectHalted();
        Assert.Equal((ushort)1, a.Top);

        // −7 mod 2 = −1 (0xFFFF)
        var b = new TestVm().Load(0x1BF9, 0x1B02, 0x0A00, 0x2400);
        b.Machine.Run();
        b.ExpectHalted();
        Assert.Equal((ushort)0xFFFF, b.Top);

        // 0x8000 mod −1 = 0
        var c = new TestVm().Load(0x1C00, 0x8000, 0x1BFF, 0x0A00, 0x2400);
        c.Machine.Run();
        c.ExpectHalted();
        Assert.Equal((ushort)0, c.Top);
    }

    [Fact]
    public void Bitwise_AndOrXor_Not()
    {
        var and = new TestVm().Load(0x1B05, 0x1B03, 0x0B00, 0x2400); // 0101 & 0011
        and.Machine.Run(); and.ExpectHalted();
        Assert.Equal((ushort)1, and.Top);

        var or = new TestVm().Load(0x1B05, 0x1B03, 0x0C00, 0x2400);
        or.Machine.Run(); or.ExpectHalted();
        Assert.Equal((ushort)7, or.Top);

        var xor = new TestVm().Load(0x1B05, 0x1B03, 0x0D00, 0x2400);
        xor.Machine.Run(); xor.ExpectHalted();
        Assert.Equal((ushort)6, xor.Top);

        var not = new TestVm().Load(0x1B05, 0x0E00, 0x2400);
        not.Machine.Run(); not.ExpectHalted();
        Assert.Equal((ushort)0xFFFA, not.Top);
    }

    [Fact]
    public void Sll_ValueOnTopCountSecond_MasksCountTo4Bits()
    {
        // 0x8000 << 1 = 0 (count = second = 1, value = top = 0x8000).
        var a = new TestVm().Load(0x1B01, 0x1C00, 0x8000, 0x0F00, 0x2400);
        a.Machine.Run(); a.ExpectHalted();
        Assert.Equal((ushort)0, a.Top);

        // count 17 → masked to 1: 1 << 1 = 2.
        var b = new TestVm().Load(0x1B11, 0x1B01, 0x0F00, 0x2400);
        b.Machine.Run(); b.ExpectHalted();
        Assert.Equal((ushort)2, b.Top);
    }

    [Fact]
    public void Slr_ArithmeticShiftSignExtends()
    {
        var vm = new TestVm().Load(0x1B01, 0x1C00, 0x8000, 0x1000, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0xC000, vm.Top);
    }

    [Fact]
    public void Slr_MasksCountTo4Bits()
    {
        // count 17 → 1: 0x8000 >> 1 = 0xC000.
        var vm = new TestVm().Load(0x1B11, 0x1C00, 0x8000, 0x1000, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0xC000, vm.Top);
    }

    // ---- Comparison (0x11–0x1A) — push 1 or 0 for (second op top) ----

    [Fact]
    public void Signed_Comparisons_Above0x8000()
    {
        // 0x8000 (−32768) vs 1: LT true signed, ULT false unsigned.
        var lt = new TestVm().Load(0x1C00, 0x8000, 0x1C00, 0x0001, 0x1300, 0x2400);
        lt.Machine.Run(); lt.ExpectHalted();
        Assert.Equal((ushort)1, lt.Top);

        var ult = new TestVm().Load(0x1C00, 0x8000, 0x1C00, 0x0001, 0x1700, 0x2400);
        ult.Machine.Run(); ult.ExpectHalted();
        Assert.Equal((ushort)0, ult.Top);

        // 1 vs 0x8000: Gt true signed, Ugt false unsigned.
        var gt = new TestVm().Load(0x1C00, 0x0001, 0x1C00, 0x8000, 0x1400, 0x2400);
        gt.Machine.Run(); gt.ExpectHalted();
        Assert.Equal((ushort)1, gt.Top);

        var ugt = new TestVm().Load(0x1C00, 0x0001, 0x1C00, 0x8000, 0x1800, 0x2400);
        ugt.Machine.Run(); ugt.ExpectHalted();
        Assert.Equal((ushort)0, ugt.Top);
    }

    [Fact]
    public void SignedVsUnsigned_LeGe_UleUge()
    {
        // −1 (0xFFFF) vs 1: Le true signed (−1 ≤ 1), Ule false (65535 ≤ 1 false).
        var le = new TestVm().Load(0x1C00, 0xFFFF, 0x1C00, 0x0001, 0x1500, 0x2400);
        le.Machine.Run(); le.ExpectHalted();
        Assert.Equal((ushort)1, le.Top);

        var ule = new TestVm().Load(0x1C00, 0xFFFF, 0x1C00, 0x0001, 0x1900, 0x2400);
        ule.Machine.Run(); ule.ExpectHalted();
        Assert.Equal((ushort)0, ule.Top);

        // Ge / Uge at the 0x8000 boundary: 0x8000 Ge 0 true signed, Uge false unsigned…
        // (second = 0x8000, top = 0: (second op top): −32768 ≥ 0? no → 0; 32768 ≥ 0? yes → 1.)
        var ge = new TestVm().Load(0x1C00, 0x8000, 0x1C00, 0x0000, 0x1600, 0x2400);
        ge.Machine.Run(); ge.ExpectHalted();
        Assert.Equal((ushort)0, ge.Top);

        var uge = new TestVm().Load(0x1C00, 0x8000, 0x1C00, 0x0000, 0x1A00, 0x2400);
        uge.Machine.Run(); uge.ExpectHalted();
        Assert.Equal((ushort)1, uge.Top);
    }

    [Fact]
    public void Eq_Ne_PatternEquality()
    {
        var eq = new TestVm().Load(0x1B05, 0x1B05, 0x1100, 0x2400);
        eq.Machine.Run(); eq.ExpectHalted();
        Assert.Equal((ushort)1, eq.Top);

        var ne = new TestVm().Load(0x1B05, 0x1B06, 0x1200, 0x2400);
        ne.Machine.Run(); ne.ExpectHalted();
        Assert.Equal((ushort)1, ne.Top);

        var neq = new TestVm().Load(0x1B05, 0x1B05, 0x1200, 0x2400);
        neq.Machine.Run(); neq.ExpectHalted();
        Assert.Equal((ushort)0, neq.Top);
    }

    // ---- Memory (0x1B–0x1E) ----

    [Fact]
    public void Push_SignExtends8BitImmediate()
    {
        var vm = new TestVm().Load(0x1BFF, 0x0400, 0x1B7F, 0x2400); // PUSH −1, DROP, PUSH 127, HALT
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0x7F, vm.Top);

        var neg = new TestVm().Load(0x1BFF, 0x2400); // PUSH −1
        neg.Machine.Run();
        neg.ExpectHalted();
        Assert.Equal((ushort)0xFFFF, neg.Top);
    }

    [Fact]
    public void Push16_PushesAbsoluteValue()
    {
        var vm = new TestVm().Load(0x1C00, 0x1234, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0x1234, vm.Top);
    }

    [Fact]
    public void Store_Load_RoundTrip()
    {
        var vm = new TestVm().Load(
            0x1C00, 0xCAFE, // value
            0x1C00, 0x0100, // address
            0x1E00,         // STORE
            0x1C00, 0x0100, // address
            0x1D00,         // LOAD
            0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0xCAFE, vm.Top);
        Assert.Equal((ushort)0xCAFE, vm.Machine.ReadMemory(0x0100));
    }

    // ---- Control flow (0x1F–0x24) ----

    [Fact]
    public void Jmp_JumpsTwoWordsAhead()
    {
        // JMP target 0x0003; the word at 0x0003 is HALT (the dead word at 0x0002
        // is a sentinel: landing on it would execute 0x0000 = DUP).
        var vm = new TestVm().Load(0x1F00, 0x0003, 0x0000, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0x0003, vm.Machine.Pc);
        Assert.Equal(2, vm.Machine.InstructionCount); // JMP + HALT
    }

    [Fact]
    public void Call_Ret_ReturnsToWordAfterCall()
    {
        // sq(x) = x*x at 0x0004; caller at 0x0000 (verified golden layout).
        var vm = new TestVm().Load(0x1B07, 0x2000, 0x0004, 0x2400, 0x0000, 0x0800, 0x2100);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)49, vm.Top);
        Assert.Equal(0, vm.Machine.CallDepth);
        Assert.Equal(6, vm.Machine.InstructionCount); // PUSH, CALL, DUP, MUL, RET, HALT
    }

    [Fact]
    public void Jz_TakenOnZero_NotTakenOnNonZero_ValueConsumedEitherWay()
    {
        // Taken: PUSH 0, JZ +1 → skips PUSH 7, lands on HALT (offset = target − (PC+1)).
        var taken = new TestVm().Load(0x1B00, 0x2201, 0x1B07, 0x2400);
        taken.Machine.Run();
        taken.ExpectHalted();
        Assert.Equal(0, taken.OperandCount); // the JZ consumed the zero

        // Not taken: PUSH 9, JZ +1 → falls through to PUSH 7.
        var notTaken = new TestVm().Load(0x1B09, 0x2201, 0x1B07, 0x2400);
        notTaken.Machine.Run();
        notTaken.ExpectHalted();
        Assert.Equal((ushort)7, notTaken.Top);
    }

    [Fact]
    public void Jnz_TakenOnNonZero_NotTakenOnZero()
    {
        var taken = new TestVm().Load(0x1B09, 0x2301, 0x1B07, 0x2400);
        taken.Machine.Run();
        taken.ExpectHalted();
        Assert.Equal(0, taken.OperandCount);

        var notTaken = new TestVm().Load(0x1B00, 0x2301, 0x1B07, 0x2400);
        notTaken.Machine.Run();
        notTaken.ExpectHalted();
        Assert.Equal((ushort)7, notTaken.Top);
    }

    [Fact]
    public void Jz_Jnz_BackwardOffsetIsNegative()
    {
        // Loop: 0x0000 PUSH 1; 0x0001 SUB; 0x0002 JZ +4 (done); 0x0003 PUSH 1… wait, use:
        // 0x0000: PUSH 3 (counter)
        // 0x0001: JNZ 0x0003 (offset +1… simpler: test a backward jump)
        // Backward: at 0x0002 JZ with operand 0xFF (−1): target = 0x0003 − 1 = 0x0002 (self).
        // Use JNZ self-loop with a counter:
        // 0x0000: PUSH 2
        // 0x0001: JNZ 0x0002 (offset 0, +0 → target 0x0002)
        // 0x0002: DROP … hmm.
        // Cleanest backward test: JZ at 0x0001, operand 0xFF → target = 0x0002 + (−1) = 0x0001 (itself).
        var vm = new TestVm().Load(0x1B00, 0x22FF, 0x2400); // PUSH 0, JZ −1 (self), HALT
        vm.Machine.Run();
        Assert.Equal(MachineState.Faulted, vm.Machine.State); // infinite self-loop? No — the JZ
        // re-executes itself forever: JZ pops… the stack is empty after the first pop,
        // so the second execution underflows. The observable: a STACKUF at the JZ.
        Assert.Equal(FaultCode.StackUf, vm.Machine.LastFault);
        Assert.Equal((ushort)0x0001, vm.Machine.FaultPc);
    }

    [Fact]
    public void Jz_TargetWrapsModulo216_IntoPortRegion()
    {
        // JZ at 0x0001, operand 0xF8 (−8): target = 0x0002 − 8 = 0xFFFA (mod 2^16) —
        // a port-region fetch, which is an ADDRF fault at the port address (spec §3 erratum).
        var vm = new TestVm().Load(0x1B00, 0x22F8);
        vm.Machine.Run();
        vm.ExpectFault(FaultCode.AddrF, 0xFFFA);
        Assert.Equal(2, vm.Machine.InstructionCount); // PUSH + JZ completed
    }

    [Fact]
    public void Halt_EntersHaltedState_CountsInstruction_LeavesPcOnHaltWord()
    {
        var haltedCode = (ushort?)null;
        var vm = new TestVm().Load(0x1B05, 0x2400);
        vm.Machine.Halted += code => haltedCode = code;
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0x0001, vm.Machine.Pc);
        Assert.Equal(2, vm.Machine.InstructionCount); // HALT counts (spec §7)
        Assert.Equal(RunStopReason.Halted, vm.Machine.LastRunStop);
        Assert.Equal((ushort)0, haltedCode);
    }

    [Fact]
    public void OperandBitsDoNotCauseIllop()
    {
        // Regression: only header bits 15–14 are ILLOP. A valid ROT with operand 0xC0
        // (bits 7–6 set in the LOW byte) must execute.
        var vm = new TestVm().Load(0x1B0A, 0x1B14, 0x1B1E, 0x03C0, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new ushort[] { 10, 30, 20 }, vm.StackTopDown());
    }
}
