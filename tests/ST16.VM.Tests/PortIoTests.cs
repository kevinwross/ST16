using ST16.VM;
using Xunit;

namespace ST16.VM.Tests;

/// <summary>
/// I/O port semantics (spec §7): 0xF000 console out (STORE low 8 bits; LOAD → 0),
/// 0xF001 console in (LOAD → next byte or 0xFFFF once exhausted; STORE no-op),
/// 0xF002 instruction counter (LOAD → low 16 bits; STORE no-op). Unmapped
/// ports throw from the debug API; the machine itself faults ADDRF.
/// </summary>
public class PortIoTests
{
    [Fact]
    public void Load_ConsoleOut_ReturnsZero()
    {
        var vm = new TestVm().Load(0x1C00, 0xF000, 0x1D00, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0, vm.Top);
    }

    [Fact]
    public void Store_ConsoleOut_OutputsLowEightBitsOnly()
    {
        var vm = new TestVm().Load(0x1C00, 0xAB2A, 0x1C00, 0xF000, 0x1E00, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal('\u002A', vm.Output.Text[0]);
        Assert.Single(vm.Output.Text);
    }

    [Fact]
    public void Store_ConsoleOut_CharVisible()
    {
        var vm = new TestVm().Load(0x1C00, 0x0041, 0x1C00, 0xF000, 0x1E00, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal("A", vm.Output.Text);
    }

    [Fact]
    public void Load_ConsoleIn_ReturnsQueuedByte()
    {
        var vm = new TestVm().Load(0x1C00, 0xF001, 0x1D00, 0x2400);
        vm.Enqueue(0x41);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0x41, vm.Top);
    }

    [Fact]
    public void Load_ConsoleIn_ExhaustedReads0xFFFFForever()
    {
        var vm = new TestVm().Load(0x1C00, 0xF001, 0x1D00, 0x1C00, 0xF001, 0x1D00, 0x2400);
        vm.Enqueue(0x41);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)0xFFFF, vm.Top);       // second LOAD: exhausted
        Assert.Equal((ushort)0x41, vm.Machine.OperandStack[1]); // first LOAD: the byte

        // A third read (via the debug port API) is still 0xFFFF.
        Assert.Equal((ushort)0xFFFF, vm.Machine.ReadPort(0xF001));
    }

    [Fact]
    public void Store_ConsoleIn_IsNoOp()
    {
        var vm = new TestVm().Load(0x1B41, 0x1C00, 0xF001, 0x1E00, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(string.Empty, vm.Output.Text);
    }

    [Fact]
    public void Store_InstructionCounterPort_IsNoOp()
    {
        var vm = new TestVm().Load(0x1B01, 0x1C00, 0xF002, 0x1E00, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
    }

    [Fact]
    public void Load_InstructionCounterPort_ReturnsCountBeforeThisInstruction()
    {
        // PUSH16 (ic 0→1), LOAD 0xF002: one instruction has completed → 1.
        var vm = new TestVm().Load(0x1C00, 0xF002, 0x1D00, 0x2400);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal((ushort)1, vm.Top);
    }

    [Fact]
    public void InstructionCounterPort_WrapsLow16Bits()
    {
        // Self-JMP loop stopped by a 65536-instruction limit: the counter has
        // wrapped through 0xFFFF → 0x0000.
        var vm = new TestVm().Load(0x1F00, 0x0000); // JMP 0x0000 (self)
        vm.Machine.InstructionLimit = 65536;
        vm.Machine.Run();
        Assert.Equal(RunStopReason.Limit, vm.Machine.LastRunStop);
        Assert.Equal(65536, vm.Machine.InstructionCount);
        Assert.Equal((ushort)0, vm.Machine.ReadPort(0xF002));
    }

    [Fact]
    public void ReadPort_UnmappedPort_Throws()
    {
        var vm = new TestVm();
        Assert.Throws<ArgumentException>(() => vm.Machine.ReadPort(0xF003));
        Assert.Throws<ArgumentException>(() => vm.Machine.ReadPort(0xFFFF));
    }

    [Fact]
    public void WritePort_ConsoleOut_WritesLowByte()
    {
        var vm = new TestVm();
        vm.Machine.WritePort(0xF000, 0x0042);
        Assert.Equal("B", vm.Output.Text);
    }

    [Fact]
    public void WritePort_UnmappedPort_Throws()
    {
        var vm = new TestVm();
        Assert.Throws<ArgumentException>(() => vm.Machine.WritePort(0xF003, 0));
    }

    [Fact]
    public void Watchpoint_StoreToConsoleOutPort_Fires()
    {
        var vm = new TestVm().Load(0x1B41, 0x1C00, 0xF000, 0x1E00, 0x2400);
        var hits = new List<ushort>();
        vm.Machine.WatchpointHit += a => hits.Add(a);
        vm.Machine.SetWatchpoint(0xF000);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal(new[] { (ushort)0xF000 }, hits);
    }

    [Fact]
    public void InputSequence_DrivesDeterministicProgram()
    {
        // Read a byte, echo it back (LOAD 0xF001 → STORE 0xF000).
        var vm = new TestVm().Load(
            0x1C00, 0xF001, 0x1D00, // LOAD
            0x1C00, 0xF000, 0x1E00, // STORE
            0x2400);
        vm.Enqueue(0x58);
        vm.Machine.Run();
        vm.ExpectHalted();
        Assert.Equal("X", vm.Output.Text);
    }
}
