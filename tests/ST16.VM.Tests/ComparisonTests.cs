namespace ST16.VM.Tests;

/// <summary>Spec §5.3 — for `a b` (a second, b top): push 1 iff a OP b.</summary>
public class ComparisonTests
{
    [Fact] public void Eq()
    {
        TestKit.AssertStack(TestKit.Step1(Op.Eq, 0, 5, 5), 1);
        TestKit.AssertStack(TestKit.Step1(Op.Eq, 0, 5, 6), 0);
        TestKit.AssertStack(TestKit.Step1(Op.Eq, 0, 0x8000, 0x8000), 1);
    }

    [Fact] public void Ne()
    {
        TestKit.AssertStack(TestKit.Step1(Op.Ne, 0, 5, 6), 1);
        TestKit.AssertStack(TestKit.Step1(Op.Ne, 0, 5, 5), 0);
    }

    [Fact]
    public void SignedComparisons_Above0x8000()
    {
        // 0xFFFF is −1 signed, 0x0001 is +1: a=−1 (second), b=1 (top)
        TestKit.AssertStack(TestKit.Step1(Op.Lt, 0, 0xFFFF, 0x0001), 1);   // −1 < 1
        TestKit.AssertStack(TestKit.Step1(Op.Gt, 0, 0xFFFF, 0x0001), 0);
        TestKit.AssertStack(TestKit.Step1(Op.Le, 0, 0xFFFF, 0x0001), 1);
        TestKit.AssertStack(TestKit.Step1(Op.Ge, 0, 0xFFFF, 0x0001), 0);
        // reversed
        TestKit.AssertStack(TestKit.Step1(Op.Lt, 0, 0x0001, 0xFFFF), 0);
        TestKit.AssertStack(TestKit.Step1(Op.Gt, 0, 0x0001, 0xFFFF), 1);
    }

    [Fact]
    public void UnsignedComparisons_Above0x8000()
    {
        // a=0xFFFF (65535 unsigned, second), b=0x0001 (top)
        TestKit.AssertStack(TestKit.Step1(Op.Ult, 0, 0xFFFF, 0x0001), 0);  // 65535 < 1? no
        TestKit.AssertStack(TestKit.Step1(Op.Ugt, 0, 0xFFFF, 0x0001), 1);  // 65535 > 1: yes
        TestKit.AssertStack(TestKit.Step1(Op.Ule, 0, 0xFFFF, 0xFFFF), 1);
        TestKit.AssertStack(TestKit.Step1(Op.Uge, 0, 0x0001, 0xFFFF), 0);
    }

    [Fact] public void Le_Ge_EqualCase()
    {
        TestKit.AssertStack(TestKit.Step1(Op.Le, 0, 4, 4), 1);
        TestKit.AssertStack(TestKit.Step1(Op.Ge, 0, 4, 4), 1);
    }
}
