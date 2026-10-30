namespace ST16.VM.Tests;

/// <summary>Spec §5.2 arithmetic — for `a b` (a second, b top): result = a OP b; wraps mod 2^16.</summary>
public class ArithmeticTests
{
    [Fact] public void Add_Sums() => TestKit.AssertStack(TestKit.Step1(Op.Add, 0, 3, 5), 8);
    [Fact] public void Add_Wraps() => TestKit.AssertStack(TestKit.Step1(Op.Add, 0, 0x7FFF, 0x7FFF), 0xFFFE);

    [Fact] public void Sub_SecondMinusTop() => TestKit.AssertStack(TestKit.Step1(Op.Sub, 0, 5, 3), 2);
    [Fact] public void Sub_TenMinusOne() => TestKit.AssertStack(TestKit.Step1(Op.Sub, 0, 100, 1), 99);
    [Fact] public void Sub_Wraps() => TestKit.AssertStack(TestKit.Step1(Op.Sub, 0, 0, 1), 0xFFFF);

    [Fact] public void Neg_Negates() => TestKit.AssertStack(TestKit.Step1(Op.Neg, 0, 5), 0xFFFB);
    [Fact] public void Neg_0x8000Wraps() => TestKit.AssertStack(TestKit.Step1(Op.Neg, 0, 0x8000), 0x8000);

    [Fact] public void Mul_MultipliesLow16() => TestKit.AssertStack(TestKit.Step1(Op.Mul, 0, 3, 5), 15);
    [Fact] public void Mul_Wraps() => TestKit.AssertStack(TestKit.Step1(Op.Mul, 0, 0x8000, 2), 0x0000);
    [Fact] public void Mul_Wraps2() => TestKit.AssertStack(TestKit.Step1(Op.Mul, 0, 0x4000, 3), 0xC000);

    [Fact] public void Div_TruncatesTowardZero()
    {
        TestKit.AssertStack(TestKit.Step1(Op.Div, 0, 7, 2), 3);
        TestKit.AssertStack(TestKit.Step1(Op.Div, 0, 0xFFF9, 2), 0xFFFD);             // −7÷2 = −3
        TestKit.AssertStack(TestKit.Step1(Op.Div, 0, 2, 0xFFF9), 0);                  // 2÷−7 = 0
        TestKit.AssertStack(TestKit.Step1(Op.Div, 0, 0xFFF9, 0xFFFE), 3);             // −7÷−2 = 3
    }

    [Fact]
    public void Div_0x8000DivideMinus1_WrapsNoFault()
    {
        var m = TestKit.Step1(Op.Div, 0, 0x8000, 0xFFFF);
        Assert.Equal(MachineState.Running, m.State);
        TestKit.AssertStack(m, 0x8000);
    }

    [Fact] public void Mod_SignOfDividend()
    {
        TestKit.AssertStack(TestKit.Step1(Op.Mod, 0, 7, 3), 1);
        TestKit.AssertStack(TestKit.Step1(Op.Mod, 0, 0xFFF9, 3), 0xFFFF);             // −7 mod 3 = −1
        TestKit.AssertStack(TestKit.Step1(Op.Mod, 0, 7, 0xFFFD), 1);                  // 7 mod −3 = 1
        TestKit.AssertStack(TestKit.Step1(Op.Mod, 0, 0xFFF9, 0xFFFD), 0xFFFF);        // −7 mod −3 = −1
    }

    [Fact] public void Mod_0x8000ModMinus1_IsZero()
        => TestKit.AssertStack(TestKit.Step1(Op.Mod, 0, 0x8000, 0xFFFF), 0);

    [Fact] public void Bitwise()
    {
        TestKit.AssertStack(TestKit.Step1(Op.And, 0, 0xF0F0, 0x0FF0), 0x00F0);
        TestKit.AssertStack(TestKit.Step1(Op.Or, 0, 0xF0F0, 0x0FF0), 0xFFF0);
        TestKit.AssertStack(TestKit.Step1(Op.Xor, 0, 0xF0F0, 0x0FF0), 0xFF00);
        TestKit.AssertStack(TestKit.Step1(Op.Not, 0, 0x1234), 0xEDCB);
    }

    [Fact]
    public void Sll_ValueOnTopCountSecond_MasksCount()
    {
        TestKit.AssertStack(TestKit.Step1(Op.Sll, 0, 1, 0x4000), 0x8000);     // 0x4000 << 1
        TestKit.AssertStack(TestKit.Step1(Op.Sll, 0, 17, 1), 2);              // count 17 -> 1
        TestKit.AssertStack(TestKit.Step1(Op.Sll, 0, 0, 5), 5);
    }

    [Fact]
    public void Slr_ArithmeticSignExtending()
    {
        TestKit.AssertStack(TestKit.Step1(Op.Slr, 0, 1, 0x8000), 0xC000);
        TestKit.AssertStack(TestKit.Step1(Op.Slr, 0, 4, 0xFFF8), 0xFFFF);     // −8 >> 4 = −1
        TestKit.AssertStack(TestKit.Step1(Op.Slr, 0, 0, 5), 5);
    }
}
