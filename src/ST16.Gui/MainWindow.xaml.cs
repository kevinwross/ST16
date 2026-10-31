using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using ST16.VM;

namespace ST16.Gui;

/// <summary>
/// The debugger window. Thin view: every state read and every state change
/// goes through <see cref="DebugSession"/>; this class only renders.
/// </summary>
public partial class MainWindow : Window
{
    private readonly DebugSession _session = new();
    private ushort[] _previousStack = [];
    private int _memoryStart;
    private const int MemoryRows = 2;
    private const int WordsPerRow = 16;

    // ---- row item classes (plain objects, bound by the XAML templates) ----

    private sealed class SourceLineItem
    {
        public int FileIndex { get; init; }
        public int LineNo { get; init; }
        public string Text { get; init; } = "";
        public bool IsCurrent { get; set; }
        public bool IsFault { get; set; }
        public Brush GutterColor { get; set; } = Brushes.Gray;
        public string GutterText { get; set; } = "·";
    }

    private sealed class DisasmLineItem
    {
        public int Address { get; init; }
        public string AddressText { get; init; } = "";
        public string Text { get; init; } = "";
        public string Comment { get; init; } = "";
        public string Refs { get; set; } = "";
        public bool IsCurrent { get; set; }
        public bool IsFault { get; set; }
        public Brush GutterColor { get; set; } = Brushes.Gray;
    }

    private sealed class StackItem
    {
        public int Slot { get; init; }
        public string Hex { get; init; } = "";
        public string Dec { get; init; } = "";
        public string Changed { get; set; } = "";
    }

    private sealed class CallItem
    {
        public int Frame { get; init; }
        public ushort Address { get; init; }
        public string ReturnText { get; init; } = "";
        public string Function { get; init; } = "";
        public string Line { get; init; } = "";
    }

    private sealed class MemoryItem
    {
        public int Address { get; init; }
        public string AddressText { get; init; } = "";
        public string WordsText { get; init; } = "";
    }

    public MainWindow()
    {
        InitializeComponent();
        _session.Changed += Refresh;
        _session.OutputAppended += () => ConsoleOutputBox.Text = _session.ConsoleOutput;
        Loaded += (_, _) => Refresh();
    }

    #region Load / run control

    private void OnLoad(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load a ST16 program",
            Filter = "ST16 sources (*.st16;*.c)|*.st16;*.c|ST16 binaries (*.bin)|*.bin|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            _session.LoadFile(dialog.FileName);
            Title = $"ST16 debugger — {System.IO.Path.GetFileName(dialog.FileName)}";
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void WithPreviousStack(Action operation)
    {
        _previousStack = _session.OperandStack.ToArray();
        operation();
    }

    private void OnRun(object sender, RoutedEventArgs e) => WithPreviousStack(_session.Run);

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (!_session.Pause())
        {
            MessageBox.Show(this,
                "The ST16 engine runs to completion synchronously (a VM is a pure function of " +
                "program + input) — there is nothing to interrupt. Use Run to the next stop, or " +
                "set the instruction limit to stop after a fixed number of instructions.",
                "Pause", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    private void OnStepIn(object sender, RoutedEventArgs e) => WithPreviousStack(_session.StepInto);
    private void OnStepOver(object sender, RoutedEventArgs e) => WithPreviousStack(_session.StepOver);
    private void OnStepOut(object sender, RoutedEventArgs e) => WithPreviousStack(_session.StepOut);
    private void OnReset(object sender, RoutedEventArgs e) => WithPreviousStack(_session.Reset);
    private void OnUndo(object sender, RoutedEventArgs e) => WithPreviousStack(_session.Undo);

    private void OnLimitKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        if (long.TryParse(LimitBox.Text, out var limit))
        {
            _session.SetInstructionLimit(limit);
            Refresh();
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.F5: OnRun(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F10: OnStepOver(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.F11: OnStepIn(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.Z when (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control:
                OnUndo(this, new RoutedEventArgs()); e.Handled = true; break;
        }
    }

    #endregion

    #region Breakpoints / navigation

    private void OnSourceGutterClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SourceLineItem item)
        {
            _session.ToggleBreakpointAtLine(item.FileIndex, item.LineNo);
            Refresh();
        }
    }

    private void OnDisasmGutterClick(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is DisasmLineItem item)
        {
            _session.ToggleBreakpoint((ushort)item.Address);
            Refresh();
        }
    }

    /// <summary>
    /// Double-click in the disassembly: jump to the symbol named in the line's
    /// comment (labels render as <c>; 0xADDR name</c>), else to the line's own
    /// address.
    /// </summary>
    private void OnDisasmDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DisasmList.SelectedItem is not DisasmLineItem item)
            return;
        var comment = item.Comment.Trim();
        var tokens = comment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 0)
        {
            var last = tokens[^1];
            if (_session.AddressOfSymbol(last) is { } addr)
            {
                _session.JumpTo(addr);
                Refresh();
                return;
            }
        }
        _session.JumpTo(item.Address);
        Refresh();
    }

    /// <summary>Double-click in the source: jump to a label declared on the line.</summary>
    private void OnCallDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CallList.SelectedItem is CallItem item)
        {
            _session.JumpTo(item.Address);
            Refresh();
        }
    }

    #endregion

    #region Memory pane

    private static int ParseAddress(string text)
    {
        text = text.Trim();
        var value = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(text[2..], System.Globalization.NumberStyles.HexNumber)
            : int.Parse(text, System.Globalization.NumberStyles.HexNumber | System.Globalization.NumberStyles.AllowDecimalPoint);
        return value & 0xEFFF;
    }

    private void OnMemGo(object sender, RoutedEventArgs e)
    {
        if (ParseAddressSafe(MemStartBox.Text, out var start))
        {
            _memoryStart = start;
            RefreshMemory();
        }
    }

    private void OnMemBaseChanged(object sender, RoutedEventArgs e) => RefreshMemory();

    private static bool ParseAddressSafe(string text, out int address)
    {
        address = 0;
        try
        {
            address = ParseAddress(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnMemApply(object sender, RoutedEventArgs e)
    {
        if (!ParseAddressSafe(MemAddrBox.Text, out var address) ||
            !ushort.TryParse(
                MemValueBox.Text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? MemValueBox.Text.Trim()[2..]
                    : MemValueBox.Text.Trim(),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value) &&
            !ushort.TryParse(MemValueBox.Text.Trim(), out value))
        {
            MessageBox.Show(this, "Address and value must be 16-bit numbers (0x prefix or hex).", "Memory",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        WithPreviousStack(() => _session.WriteWord((ushort)address, value));
        Refresh();
    }

    private void OnMemWatchChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || MemAddrBox is null)
            return;
        if (ParseAddressSafe(MemAddrBox.Text, out var address))
            _session.ToggleWatchpoint((ushort)address);
    }

    private void RefreshMemory()
    {
        // IsChecked="True" on the hex box fires this during XAML parsing,
        // before the other named controls exist.
        if (!IsInitialized || MemList is null)
            return;
        var hex = MemHexBox.IsChecked == true;
        var items = new List<MemoryItem>(MemoryRows);
        for (int r = 0; r < MemoryRows; r++)
        {
            int baseAddr = _memoryStart + r * WordsPerRow;
            var words = _session.MemoryWindow((ushort)(baseAddr & 0xEFFF), WordsPerRow);
            var cells = new string[words.Length];
            for (int i = 0; i < words.Length; i++)
                cells[i] = hex ? $"0x{words[i]:X4}" : words[i].ToString();
            items.Add(new MemoryItem
            {
                Address = baseAddr,
                AddressText = $"0x{baseAddr:X4}",
                WordsText = string.Join("  ", cells),
            });
        }
        MemList.ItemsSource = items;
    }

    #endregion

    #region Console pane

    private void OnSendInput(object sender, RoutedEventArgs e)
    {
        _session.EnqueueInput(ConsoleInputBox.Text);
        ConsoleInputBox.Clear();
        Refresh();
    }

    private void OnConsoleInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSendInput(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnClearConsole(object sender, RoutedEventArgs e)
    {
        _session.ClearConsole();
        Refresh();
    }

    #endregion

    #region Rendering

    /// <summary>Rebuilds every pane from the session state.</summary>
    private void Refresh()
    {
        var state = _session.State;

        // status bar
        StatusPc.Text = $"pc=0x{_session.Pc:X4}";
        StatusIc.Text = $"ic={_session.InstructionCount}";
        StatusState.Text = $"state={state}";
        StatusLine.Text = _session.LineForAddress(_session.Pc) is { } fl
            ? $"line {fl.Line}"
            : "";
        StatusFault.Text = _session.FaultDescription ?? "";
        StatusAnnotation.Text = _session.CurrentInstructionAnnotation ?? "";

        // toolbar enablement
        bool runnable = state is MachineState.Paused or MachineState.Running;
        RunButton.IsEnabled = runnable;
        StepInButton.IsEnabled = runnable;
        StepOverButton.IsEnabled = runnable;
        StepOutButton.IsEnabled = runnable;
        UndoButton.IsEnabled = _session.CanUndo;

        RefreshSource();
        RefreshDisassembly();
        RefreshStack();
        RefreshCalls();
        RefreshMemory();
        ConsoleOutputBox.Text = _session.ConsoleOutput;
    }

    private void RefreshSource()
    {
        var sources = _session.Sources;
        if (sources is null || sources.Count == 0)
        {
            SourceTabs.Items.Clear();
            return;
        }

        var current = _session.LineForAddress(_session.Pc);
        var currentFile = current?.File ?? -1;
        var currentLine = current?.Line ?? -1;
        var faultLine = _session.State == MachineState.Faulted
            ? _session.LineForAddress(_session.FaultPc)
            : null;

        var tab = (TabItem)SourceTabs.Items[0];
        tab.Header = sources[0].Name;
        var lines = sources[0].Text.Split('\n');
        var items = new List<SourceLineItem>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            var item = new SourceLineItem
            {
                FileIndex = 0,
                LineNo = lineNo,
                Text = lines[i].TrimEnd('\r'),
                IsCurrent = lineNo == currentLine,
                IsFault = faultLine is not null && faultLine.Value.Line == lineNo,
            };
            items.Add(item);
        }
        SourceList.ItemsSource = items;
        if (currentFile == 0 && items.Any(i => i.IsCurrent))
            SourceList.SelectedItem = items.First(i => i.IsCurrent);
    }

    private void RefreshDisassembly()
    {
        var disasm = _session.Disassembly;
        if (disasm is null)
        {
            DisasmList.ItemsSource = null;
            return;
        }
        var items = new List<DisasmLineItem>(disasm.Lines.Count);
        foreach (var line in disasm.Lines)
        {
            var item = new DisasmLineItem
            {
                Address = line.Address,
                AddressText = $"0x{line.Address:X4}",
                Text = line.Text,
                Comment = line.Comment ?? "",
                IsCurrent = line.Address == _session.Pc,
                IsFault = _session.State == MachineState.Faulted && line.Address == _session.FaultPc,
                GutterColor = _session.HasBreakpoint((ushort)line.Address) ? Brushes.Red : Brushes.Gray,
            };
            var calls = _session.CallReferencesTo(line.Address);
            var branches = _session.BranchReferencesTo(line.Address);
            var refs = new List<string>();
            foreach (var a in calls)
                refs.Add($"called from 0x{a:X4}");
            foreach (var a in branches)
                refs.Add($"branched from 0x{a:X4}");
            item.Refs = string.Join("\n", refs);
            items.Add(item);
        }
        DisasmList.ItemsSource = items;
        if (items.Any(i => i.IsCurrent))
            DisasmList.SelectedItem = items.First(i => i.IsCurrent);
    }

    private void RefreshStack()
    {
        var stack = _session.OperandStack;
        var capacity = 32;
        var items = new List<StackItem>(capacity);
        for (int i = 0; i < capacity; i++)
        {
            ushort value = i < stack.Count ? stack[i] : (ushort)0;
            string changed = "";
            if (i < _previousStack.Length)
            {
                changed = i < stack.Count && _previousStack[i] != value ? "·"
                    : i >= stack.Count && _previousStack[i] != 0 ? "↓" : "";
            }
            items.Add(new StackItem
            {
                Slot = i + 1,
                Hex = $"0x{value:X4}",
                Dec = ((short)value).ToString(),
                Changed = changed,
            });
        }
        StackList.ItemsSource = items;
    }

    private void RefreshCalls()
    {
        var frames = _session.CallStack;
        var items = new List<CallItem>(frames.Count);
        for (int i = 0; i < frames.Count; i++)
        {
            var (function, line) = _session.ResolveFrame(frames[i]);
            items.Add(new CallItem
            {
                Frame = frames.Count - i,
                Address = frames[i],
                ReturnText = $"0x{frames[i]:X4}",
                Function = function,
                Line = line?.ToString() ?? "",
            });
        }
        CallList.ItemsSource = items;
    }

    #endregion
}
