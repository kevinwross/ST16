namespace ST16.VM.Tests;

/// <summary>Opcodes (spec §5).</summary>
internal static class Op
{
    public const ushort Dup = 0x00, Swap = 0x01, Over = 0x02, Rot = 0x03, Drop = 0x04;
    public const ushort Add = 0x05, Sub = 0x06, Neg = 0x07, Mul = 0x08, Div = 0x09, Mod = 0x0A;
    public const ushort And = 0x0B, Or = 0x0C, Xor = 0x0D, Not = 0x0E, Sll = 0x0F, Slr = 0x10;
    public const ushort Eq = 0x11, Ne = 0x12, Lt = 0x13, Gt = 0x14, Le = 0x15, Ge = 0x16;
    public const ushort Ult = 0x17, Ugt = 0x18, Ule = 0x19, Uge = 0x1A;
    public const ushort Push = 0x1B, Push16 = 0x1C, Load = 0x1D, Store = 0x1E;
    public const ushort Jmp = 0x1F, Call = 0x20, Ret = 0x21, Jz = 0x22, Jnz = 0x23, Halt = 0x24;
}

internal static class TestKit
{
    /// <summary>Encode a header word: opcode in the high byte, 8-bit operand low (spec §4 erratum).</summary>
    public static ushort Encode(ushort op, ushort imm = 0) => (ushort)((op << 8) | (imm & 0xFF));

    /// <summary>Build a machine, load a program at 0x0000, and seed the operand stack (bottom→top given).</summary>
    public static ST16Machine Machine(ushort[] program, params ushort[] stackBottomToTop)
    {
        var m = new ST16Machine();
        m.LoadProgram(program);
        if (stackBottomToTop.Length > 0)
        {
            var snap = m.TakeSnapshot();
            var topFirst = new ushort[stackBottomToTop.Length];
            for (int i = 0; i < topFirst.Length; i++)
                topFirst[i] = stackBottomToTop[topFirst.Length - 1 - i];
            m.Restore(new MachineSnapshot
            {
                PC = snap.PC,
                State = snap.State,
                StopReason = snap.StopReason,
                InstructionCount = snap.InstructionCount,
                OperandStack = topFirst,
                CallStack = snap.CallStack,
                Memory = snap.Memory,
                Breakpoints = snap.Breakpoints,
            });
        }
        return m;
    }

    /// <summary>Load one instruction at PC 0, seed the stack, execute it, return the machine.</summary>
    public static ST16Machine Step1(ushort op, ushort imm, params ushort[] stackBottomToTop)
    {
        var m = Machine(new[] { Encode(op, imm) }, stackBottomToTop);
        m.Step();
        return m;
    }

    /// <summary>Seed the call stack (most recent return address first) via snapshot restore.</summary>
    public static void SeedCallStack(ST16Machine m, params ushort[] returnAddressesMostRecentFirst)
    {
        var snap = m.TakeSnapshot();
        m.Restore(new MachineSnapshot
        {
            PC = snap.PC,
            State = snap.State,
            StopReason = snap.StopReason,
            InstructionCount = snap.InstructionCount,
            OperandStack = snap.OperandStack,
            CallStack = returnAddressesMostRecentFirst,
            Memory = snap.Memory,
            Breakpoints = snap.Breakpoints,
        });
    }

    /// <summary>Assert the stack result: values top→top.</summary>
    public static void AssertStack(ST16Machine m, params ushort[] expectedTopFirst)
    {
        var actual = m.OperandStack;
        Assert.Equal(expectedTopFirst.Length, actual.Count);
        for (int i = 0; i < expectedTopFirst.Length; i++)
            Assert.Equal(expectedTopFirst[i], actual[i]);
    }
}
