using ST16.CC;
using ST16.Gui;
using ST16.VM;
using Xunit;

namespace ST16.Gui.Tests;

/// <summary>
/// Integration tests for the debugger session (the GUI's logic layer):
/// load a phase-4 sample through the same path the window uses, set
/// breakpoints at known symbols/lines, step, inspect stack and memory,
/// and continue to completion. No pixels — the WPF shell renders what
/// this layer exposes.
/// </summary>
public class GuiSessionTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ST16.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("ST16.slnx not found above " + AppContext.BaseDirectory);
    }

    private static string SamplePath(string dir, string file) =>
        Path.Combine(RepoRoot(), "samples", dir, file);

    private static DebugSession LoadSample(string dir, string file)
    {
        var session = new DebugSession();
        session.LoadFile(SamplePath(dir, file));
        return session;
    }

    /// <summary>
    /// Compiles a C sample into a temp directory as <c>.bin</c> + <c>.st16d</c>
    /// (the artifacts <c>st16 cc</c> would produce) and loads it by its
    /// <c>.c</c> path, exactly as the window's Load dialog does.
    /// </summary>
    private static DebugSession LoadC(string dir, string file)
    {
        var baseName = Path.GetFileNameWithoutExtension(file);
        var tmp = Path.Combine(RepoRoot(), ".tmp-smoke", "gui-tests", dir);
        Directory.CreateDirectory(tmp);
        try
        {
            var source = File.ReadAllText(SamplePath(dir, file));
            var output = CCCompiler.Compile(source, file);
            File.WriteAllBytes(Path.Combine(tmp, baseName + ".bin"), output.Binary);
            File.WriteAllText(Path.Combine(tmp, baseName + ".st16d"), output.St16d.ToJson());
            File.WriteAllText(Path.Combine(tmp, file), source);

            var session = new DebugSession();
            session.LoadFile(Path.Combine(tmp, file));
            return session;
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
        }
    }

    // ------------------------------------------------------------------
    // Load + run control
    // ------------------------------------------------------------------

    [Fact]
    public void Sq_Steps_Into_And_Over_Call()
    {
        var session = LoadSample("02-sq", "sq.st16");
        Assert.Equal(MachineState.Paused, session.State);

        // main: PUSH 7 (pc 0) then CALL sq (pc 1, two words)
        session.StepInto();
        Assert.Equal((ushort)1, session.Pc);
        Assert.Equal(7, session.OperandStack[0]);

        // StepOver the CALL: the whole sq frame runs, result lands on the stack
        session.StepOver();
        Assert.Equal(49, session.OperandStack[0]);
        Assert.Equal(MachineState.Paused, session.State);

        session.Run();
        Assert.Equal(MachineState.Halted, session.State);
        Assert.Equal("1", session.ConsoleOutput);
    }

    [Fact]
    public void Hello_Breakpoint_At_Label_And_Run_To_Halt()
    {
        var session = LoadSample("01-hello", "hello.st16");
        var end = session.AddressOfSymbol("end");
        Assert.NotNull(end);
        session.SetBreakpoint((ushort)end!.Value);
        Assert.True(session.HasBreakpoint((ushort)end.Value));

        session.Run();
        Assert.Equal(MachineState.Paused, session.State);
        Assert.Equal(end.Value, session.Pc);
        Assert.Equal(RunStopReason.Breakpoint, session.LastRunStop);

        session.ClearBreakpoint((ushort)end.Value);
        session.Run();
        Assert.Equal(MachineState.Halted, session.State);
        Assert.Equal("Hello, ST16!\n", session.ConsoleOutput);
    }

    [Fact]
    public void Fib_Breakpoint_At_Function_Entry_Sees_Call_Stack()
    {
        var session = LoadSample("04-fib", "fib.st16");
        var entry = session.AddressOfSymbol("fib_r");
        Assert.NotNull(entry);
        session.SetBreakpoint((ushort)entry!.Value);

        session.Run();
        Assert.Equal(MachineState.Paused, session.State);
        Assert.Equal(entry.Value, session.Pc);
        Assert.True(session.CallStack.Count >= 1, "the recursive call should have a frame");

        // pure-assembly program: the top-level code is not a CALL target, so the
        // frame resolves to the nearest label (design: frame / label / line).
        var (function, _) = session.ResolveFrame(session.CallStack[0]);
        Assert.NotEqual("?", function);
        Assert.NotNull(session.AddressOfSymbol(function));

        // breakpoints re-fire: clear it before continuing to completion
        session.ClearBreakpoint((ushort)entry.Value);
        session.Run();
        Assert.Equal(MachineState.Halted, session.State);
        Assert.Equal("55 55\n", session.ConsoleOutput);
    }

    [Fact]
    public void Calc_Console_Input_And_Output()
    {
        var session = LoadSample("06-calc", "calc.st16");
        session.EnqueueInput("7*8=");
        session.Run();
        Assert.Equal(MachineState.Halted, session.State);
        Assert.Equal("56\n", session.ConsoleOutput);
    }

    [Fact]
    public void Gcd_C_Sample_Breakpoint_At_Function_And_Completes()
    {
        var session = LoadC("08-gcd", "gcd.c");
        var entry = session.AddressOfSymbol("gcd");
        Assert.NotNull(entry);
        session.SetBreakpoint((ushort)entry!.Value);

        session.Run();
        Assert.Equal(MachineState.Paused, session.State);
        Assert.Equal(entry.Value, session.Pc);
        Assert.True(session.CallStack.Count >= 1);

        session.Run();
        Assert.Equal(MachineState.Halted, session.State);
        Assert.Equal("good\n", session.ConsoleOutput);
    }

    [Fact]
    public void Stringrev_C_Sample_Sources_Embedded_And_Output()
    {
        var session = LoadC("09-stringrev", "stringrev.c");
        // the .st16d from st16cc embeds the .c source for the source pane
        Assert.NotNull(session.Sources);
        Assert.Contains("abcde", session.Sources![0].Text);

        session.Run();
        Assert.Equal(MachineState.Halted, session.State);
        Assert.Equal("edcba\n", session.ConsoleOutput);
    }

    // ------------------------------------------------------------------
    // Undo / memory / limits / jump
    // ------------------------------------------------------------------

    [Fact]
    public void Undo_Restores_Pc_And_Stack()
    {
        var session = LoadSample("02-sq", "sq.st16");
        session.StepInto();          // PUSH 7
        var pcBeforeCall = session.Pc;
        var stackBefore = session.OperandStack.ToArray();

        session.StepInto();          // CALL sq (jumps into the function)
        Assert.NotEqual(pcBeforeCall, session.Pc);

        session.Undo();
        Assert.Equal(pcBeforeCall, session.Pc);
        Assert.Equal(stackBefore, session.OperandStack.ToArray());
    }

    [Fact]
    public void Memory_Edit_Is_Visible_And_Undoable()
    {
        var session = LoadSample("01-hello", "hello.st16");
        var msg = session.AddressOfSymbol("msg");
        Assert.NotNull(msg);
        Assert.Equal((ushort)'H', session.ReadWord((ushort)msg!.Value));

        session.WriteWord((ushort)msg.Value, (ushort)'J');
        Assert.Equal((ushort)'J', session.ReadWord((ushort)msg.Value));

        session.Undo();
        Assert.Equal((ushort)'H', session.ReadWord((ushort)msg.Value));
    }

    [Fact]
    public void Instruction_Limit_Stops_Run()
    {
        var session = LoadSample("03-starloop", "starloop.st16");
        session.SetInstructionLimit(5);
        session.Run();
        Assert.Equal(MachineState.Paused, session.State);
        Assert.Equal(5, session.InstructionCount);
        Assert.Equal(RunStopReason.Limit, session.LastRunStop);

        session.SetInstructionLimit(0);
        session.Run();
        Assert.Equal(MachineState.Halted, session.State);
        Assert.Equal("**********", session.ConsoleOutput);
    }

    [Fact]
    public void JumpTo_Moves_Pc_And_Preserves_Other_State()
    {
        var session = LoadSample("04-fib", "fib.st16");
        session.StepInto();          // PUSH 0 (iterative part)
        var fibEntry = session.AddressOfSymbol("fib_r");
        Assert.NotNull(fibEntry);

        session.JumpTo(fibEntry!.Value);
        Assert.Equal(fibEntry.Value, session.Pc);
        Assert.Equal(MachineState.Paused, session.State);
        Assert.Equal(1, session.OperandStackCount);
        Assert.Equal(0, session.OperandStack[0]);
    }

    // ------------------------------------------------------------------
    // Breakpoint bookkeeping + watchpoints
    // ------------------------------------------------------------------

    [Fact]
    public void Breakpoint_Toggle_And_Line_Mapping()
    {
        var session = LoadSample("01-hello", "hello.st16");
        Assert.Empty(session.Breakpoints);

        // the `next:` label sits on a mapped source line: toggle by address,
        // then verify the line mapping agrees.
        var next = (ushort)session.AddressOfSymbol("next")!.Value;
        session.ToggleBreakpoint(next);
        Assert.Contains(next, session.Breakpoints);
        var line = session.LineForAddress(next);
        Assert.NotNull(line);
        session.ToggleBreakpointAtLine(line!.Value.File, line.Value.Line);
        Assert.Empty(session.Breakpoints);

        session.ToggleWatchpoint(0xE000);
        Assert.Contains((ushort)0xE000, session.Watchpoints);
        session.ToggleWatchpoint(0xE000);
        Assert.Empty(session.Watchpoints);
    }

    [Fact]
    public void Fault_Discipline_Stack_Underflow_Is_Reported()
    {
        // hand-built program: DROP on an empty stack
        var asm = "main:\n    DROP\n    HALT\n";
        var words = ST16.Assembler.St16Assembler.Assemble(asm, "fault.st16").Words.ToArray();
        var session = new DebugSession();
        session.Load(words, null, asm, "fault.st16");
        session.Run();
        Assert.Equal(MachineState.Faulted, session.State);
        Assert.Equal(FaultCode.StackUf, session.LastFault);
        Assert.Contains("DROP", session.FaultDescription);
        Assert.Equal(0, session.Pc);
    }
}
