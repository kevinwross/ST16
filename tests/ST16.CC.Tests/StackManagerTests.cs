using ST16.CC;
using Xunit;

namespace ST16.CC.Tests;

/// <summary>
/// Unit tests for the operand-stack bookkeeping + spill/reload logic
/// (the machine has 32 slots; the generator keeps 28 live values and
/// spills the rest into frame spill slots).
/// </summary>
public class StackManagerTests
{
    private sealed class Recorder
    {
        public readonly List<string> Lines = new();
        public CStackManager Manager { get; }

        public Recorder(int spillBudget = 16)
            => Manager = new CStackManager(Lines.Add, spillSlotBase: 100, maxDepth: 28, spillBudget);

        public void PushValue(int n) => Manager.Push($"PUSH {n}");
        public int Count => Lines.Count;
        public string Text => string.Join("\n", Lines);
    }

    [Fact]
    public void ShallowExpression_NeverSpills()
    {
        var r = new Recorder();
        for (int i = 0; i < 28; i++)
            r.PushValue(i);
        Assert.Equal(28, r.Manager.LiveCount);
        Assert.DoesNotContain("ROT", r.Lines);
    }

    [Fact]
    public void TwentyNinthPush_SpillsOldest_WithRotAndStoreIdiom()
    {
        var r = new Recorder();
        for (int i = 0; i < 29; i++)
            r.PushValue(i);

        Assert.Equal(28, r.Manager.LiveCount);
        // Spill idiom: ROT, then PUSH16 0xE000; LOAD; PUSH <slot>; ADD; STORE.
        var rotIndex = r.Lines.IndexOf("ROT");
        Assert.True(rotIndex >= 0, "expected a ROT to spill the oldest value");
        Assert.Equal("PUSH16 0xE000", r.Lines[rotIndex + 1]);
        Assert.Equal("LOAD", r.Lines[rotIndex + 2]);
        Assert.Equal("PUSH 100", r.Lines[rotIndex + 3]); // first spill slot
        Assert.Equal("ADD", r.Lines[rotIndex + 4]);
        Assert.Equal("STORE", r.Lines[rotIndex + 5]);
    }

    [Fact]
    public void ConsumingDownToSpilledValue_ReloadsIt()
    {
        var r = new Recorder();
        for (int i = 0; i < 29; i++)
            r.PushValue(i); // value 0 is spilled into slot 100
        Assert.Single(r.Lines, l => l == "ROT");

        // Consume the 28 live values one by one (DROP). Only the spilled
        // hole remains — LiveCount is 0, but the entry is still there.
        for (int i = 0; i < 28; i++)
            r.Manager.Drop();

        Assert.Equal(0, r.Manager.LiveCount);

        // Dropping the last (spilled) value just forgets the slot: no
        // reload, no DROP instruction — the spilled value is dead.
        int before = r.Lines.Count;
        r.Manager.Drop();
        Assert.Equal(before, r.Lines.Count);
        Assert.Equal(0, r.Manager.LiveCount);
    }

    [Fact]
    public void BinaryOp_OnSpilledSecond_ReloadsSwapsAndEmits()
    {
        var r = new Recorder();
        for (int i = 0; i < 29; i++)
            r.PushValue(i); // 0 spilled into slot 100; 28 live

        // 27 binary ops consume live values only (each Op nets one entry):
        // [hole, live, live] remains after 26 ops... after 27: [hole, live].
        for (int i = 0; i < 27; i++)
            r.Manager.Op("ADD");
        Assert.Equal(1, r.Manager.LiveCount);

        // The 28th op needs the spilled value as its second operand: the
        // manager must reload it (LOAD idiom) and SWAP it back under the
        // live value before emitting the instruction.
        int before = r.Lines.Count;
        r.Manager.Op("SUB");
        var tail = r.Lines.Skip(before).ToArray();
        Assert.Equal(7, tail.Length);
        Assert.Equal("PUSH16 0xE000", tail[0]);
        Assert.Equal("LOAD", tail[1]);
        Assert.Equal("PUSH 100", tail[2]);
        Assert.Equal("ADD", tail[3]);
        Assert.Equal("LOAD", tail[4]);
        Assert.Equal("SWAP", tail[5]);
        Assert.Equal("SUB", tail[6]);
        Assert.Equal(1, r.Manager.LiveCount);
    }

    [Fact]
    public void OpWithSingleTrackedValue_DoesNotCrash()
    {
        var r = new Recorder();
        r.PushValue(1);
        r.PushValue(2);
        r.Manager.Op("ADD"); // [result]
        Assert.Equal(1, r.Manager.LiveCount);
        // A second op on the single value: the machine would STACKUF;
        // the manager must not throw an index error first.
        r.Manager.Op("ADD");
    }

    [Fact]
    public void SpillBudgetExceeded_ThrowsComplexExpression()
    {
        var r = new Recorder(spillBudget: 2);
        // 30 pushes → exactly 2 concurrent spills (at the 29th and 30th).
        for (int i = 0; i < 30; i++)
            r.PushValue(i);
        // The 31st push would spill a third value → over budget.
        var ex = Assert.Throws<CCException>(() => r.PushValue(30));
        Assert.Contains("expression too complex", ex.Diagnostic.Message);
    }

    [Fact]
    public void SwapAndStore_KeepCountsBalanced()
    {
        var r = new Recorder();
        r.PushValue(1);
        r.PushValue(2);
        r.Manager.Swap();
        Assert.Equal(2, r.Manager.LiveCount);
        r.Manager.Store(); // consumes both
        Assert.Equal(0, r.Manager.LiveCount);

        // LOAD on an empty manager would be a desync; guard: push an
        // address first (the codegen invariant: LOAD/STORE follow a
        // tracked address value).
        r.PushValue(3); // address
        r.Manager.Load();
        Assert.Equal(1, r.Manager.LiveCount);
    }
}
