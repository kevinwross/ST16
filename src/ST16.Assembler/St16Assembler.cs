using ST16.VM;

namespace ST16.Assembler;

/// <summary>The outcome of assembling one or more source files.</summary>
public sealed class AssembleResult
{
    internal AssembleResult(
        bool success,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<ushort> words,
        St16dFile? debugSymbols)
    {
        Success = success;
        Diagnostics = diagnostics;
        Words = words;
        DebugSymbols = debugSymbols;
    }

    /// <summary>True when no error diagnostics were produced.</summary>
    public bool Success { get; }

    /// <summary>All diagnostics (errors and warnings), in source order.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// The assembled words, from <c>0x0000</c> through the last written word
    /// (leading zeros for a program that starts at <c>.org</c> &gt; 0 are included).
    /// </summary>
    public IReadOnlyList<ushort> Words { get; }

    /// <summary>The <c>.st16d</c> v1 document (null when assembly failed).</summary>
    public St16dFile? DebugSymbols { get; }

    /// <summary>The words as a little-endian byte image (spec §8).</summary>
    public byte[] ToBinary()
    {
        var bytes = new byte[Words.Count * 2];
        for (int i = 0; i < Words.Count; i++)
        {
            bytes[2 * i] = (byte)(Words[i] & 0xFF);
            bytes[2 * i + 1] = (byte)(Words[i] >> 8);
        }
        return bytes;
    }
}

/// <summary>
/// The ST16 assembler (spec §8): two-pass assembly of
/// <c>[label:] mnemonic [operand] ; comment</c> lines into a little-endian
/// word image, with line-numbered diagnostics and <c>.st16d</c> v1 emission.
/// </summary>
public static class St16Assembler
{
    /// <summary>RAM word count: 61,440 words at 0x0000–0xEFFF (spec §3).</summary>
    public const int RamSize = 0xF000;

    /// <summary>Highest writable RAM address.</summary>
    public const int LastAddress = 0xEFFF;

    /// <summary>Assemble a single source file.</summary>
    public static AssembleResult Assemble(string source, string fileName = "program.st16")
        => AssembleMany(new[] { new SourceFile(fileName, source) });

    /// <summary>Assemble several source files into one address space (labels are global).</summary>
    public static AssembleResult AssembleMany(IEnumerable<SourceFile> files)
    {
        var list = files.ToList();
        if (list.Count == 0)
            throw new ArgumentException("At least one source file is required.", nameof(files));
        return new Assembler(list).Run();
    }

    /// <summary>One parsed source line (label, instruction, or directive).</summary>
    private sealed class Stmt
    {
        public int File;
        public int Line;
        public int Column;
        public string? Label;

        // Instruction form (exactly one of instruction / directive).
        public Opcode? Mnemonic;
        public OperandKind OperandKind;
        public Expression? OperandExpr;   // null = no operand

        // Directive form.
        public string? Directive;         // "org", "dw", "db", "ds"
        public string? DirectiveArg;      // raw text after the directive
        public List<Expression>? DwExprs; // parsed .dw expressions

        public bool IsInstruction => Mnemonic is not null;
        public bool IsDataDirective => Directive is "dw" or "db" or "ds";

        // Filled in pass 1.
        public int Size = 0;              // words written (0 = none; set once)
        public int Address = -1;          // start address (set once)
    }

    private sealed class Assembler
    {
        private readonly List<SourceFile> _files;
        private readonly List<Diagnostic> _diagnostics = new();
        private readonly List<Stmt> _stmts = new();
        private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
        private readonly List<LineInfo> _lineInfos = new();
        private readonly HashSet<int> _written = new();
        private readonly Dictionary<int, int> _writtenLine = new(); // word address → line (first writer)
        private readonly List<(string Name, int Address)> _labelSymbols = new();
        private readonly HashSet<int> _callTargets = new();
        private readonly HashSet<int> _dataWords = new(); // words written by .dw/.db/.ds
        private readonly ushort[] _words = new ushort[St16Assembler.RamSize];
        private int _lastWritten = -1;

        internal Assembler(List<SourceFile> files) => _files = files;

        private static readonly Dictionary<string, (Opcode Op, OpcodeInfo Info)> Mnemonics =
            Enum.GetValues<Opcode>()
                .ToDictionary(op => OpcodeTable.Get(op).Mnemonic, op => (op, OpcodeTable.Get(op)), StringComparer.OrdinalIgnoreCase);

        public AssembleResult Run()
        {
            ParseAll();
            if (HasErrors)
                return Fail();

            Pass1();
            if (HasErrors)
                return Fail();

            Pass2();
            if (HasErrors)
                return Fail();

            return new AssembleResult(true, _diagnostics.ToList(), BuildWords(), BuildSymbols());
        }

        private bool HasErrors => _diagnostics.Any(d => d.Severity == Severity.Error);

        private AssembleResult Fail()
            => new(false, _diagnostics.ToList(), Array.Empty<ushort>(), null);

        private void Error(int line, int column, string message)
            => _diagnostics.Add(new Diagnostic(Severity.Error, line, column, message));

        // ------------------------------------------------------------------
        // Parsing: source lines → stmts (with syntax + expression checks).
        // ------------------------------------------------------------------

        private void ParseAll()
        {
            for (int fileIndex = 0; fileIndex < _files.Count; fileIndex++)
            {
                var lines = _files[fileIndex].Text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                    ParseLine(fileIndex, i + 1, lines[i]);
            }
        }

        private void ParseLine(int fileIndex, int line, string rawLine)
        {
            int commentStart = rawLine.IndexOf(';');
            string code = (commentStart >= 0 ? rawLine[..commentStart] : rawLine).Trim();
            if (code.Length == 0)
                return;

            int column = rawLine.Length - rawLine.TrimStart(' ', '\t').Length + 1;
            var tokens = code.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Label: "name:" or "name :"
            string? label = null;
            int tokenStart = 0;
            if (tokens.Length > 0 && tokens[0].EndsWith(':') && tokens[0].Length > 1)
            {
                label = tokens[0][..^1];
                tokenStart = 1;
                if (!Expression.IsValidLabel(label))
                {
                    Error(line, column, $"'{tokens[0]}' is not a valid label");
                    return;
                }
            }
            else if (tokens.Length > 1 && tokens[1] == ":" && tokens[0].Length > 0 && !tokens[0].StartsWith('.')
                     && Expression.IsValidLabel(tokens[0]))
            {
                label = tokens[0];
                tokenStart = 2;
            }

            if (tokenStart >= tokens.Length)
            {
                if (label is not null)
                    _stmts.Add(new Stmt { File = fileIndex, Line = line, Column = column, Label = label });
                return;
            }

            var first = tokens[tokenStart];
            var rest = string.Join(' ', tokens[(tokenStart + 1)..]);

            if (first.StartsWith('.'))
            {
                var directive = first[1..].ToLowerInvariant();
                if (directive is not ("org" or "dw" or "db" or "ds"))
                {
                    Error(line, column, $"'{first}' is not a known directive (.org, .dw, .db, .ds)");
                    return;
                }
                var s = new Stmt
                {
                    File = fileIndex,
                    Line = line,
                    Column = column,
                    Label = label,
                    Directive = directive,
                    DirectiveArg = rest,
                };
                CheckDirectiveExprs(s);
                _stmts.Add(s);
                return;
            }

            if (!Mnemonics.TryGetValue(first, out var info))
            {
                Error(line, column, $"'{first}' is not a known mnemonic");
                return;
            }

            var s2 = new Stmt
            {
                File = fileIndex,
                Line = line,
                Column = column,
                Label = label,
                Mnemonic = info.Op,
                OperandKind = info.Info.Operand,
            };

            if (rest.Length > 0)
            {
                if (info.Info.Operand == OperandKind.Ignored)
                {
                    Error(line, column, $"{info.Info.Mnemonic} does not take an operand");
                    return;
                }
                if (!Expression.TryParse(rest, out var expr, out var err))
                {
                    Error(line, column, err ?? $"'{rest}' is not a valid operand");
                    return;
                }
                s2.OperandExpr = expr;
            }

            _stmts.Add(s2);
        }

        /// <summary>Parse directive arguments into expressions (diagnostics on failure).</summary>
        private void CheckDirectiveExprs(Stmt s)
        {
            var arg = s.DirectiveArg ?? "";
            switch (s.Directive)
            {
                case "org":
                case "ds":
                    if (!Expression.TryParse(arg, out var orgExpr, out var orgErr))
                        Error(s.Line, s.Column, orgErr ?? $"'{arg}' is not a valid expression");
                    break;
                case "dw":
                {
                    var parts = SplitArgs(arg);
                    if (parts.Count == 0)
                    {
                        Error(s.Line, s.Column, ".dw takes at least one expression");
                        break;
                    }
                    var exprs = new List<Expression>(parts.Count);
                    foreach (var part in parts)
                    {
                        if (!Expression.TryParse(part, out var e, out var err))
                        {
                            Error(s.Line, s.Column, err ?? $"'{part}' is not a valid expression");
                            return;
                        }
                        exprs.Add(e);
                    }
                    s.DwExprs = exprs;
                    break;
                }
                case "db":
                {
                    var parts = SplitArgs(arg);
                    if (parts.Count == 0 || parts.Count > 2)
                    {
                        Error(s.Line, s.Column, ".db takes one or two byte values (0–255)");
                        return;
                    }
                    foreach (var part in parts)
                    {
                        if (!Expression.TryParse(part, out var dbExpr, out var dbErr) || !dbExpr.IsConstant)
                        {
                            Error(s.Line, s.Column, dbErr ?? $".db takes byte values (0–255); got '{part}'");
                            return;
                        }
                    }
                    break;
                }
            }
        }

        private static List<string> SplitArgs(string text)
            => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        // ------------------------------------------------------------------
        // Pass 1: labels, undefined references, addresses, overlaps.
        // ------------------------------------------------------------------

        private void Pass1()
        {
            // Register labels in source order (duplicate check).
            foreach (var s in _stmts)
            {
                if (s.Label is null)
                    continue;
                if (_labels.ContainsKey(s.Label))
                    Error(s.Line, s.Column, $"duplicate label '{s.Label}'");
                else
                    _labels[s.Label] = -1; // filled below
            }

            // Undefined-label check (all labels are now registered).
            foreach (var s in _stmts)
            {
                CheckUndefined(s.OperandExpr, s);
                if (s.DwExprs is not null)
                    foreach (var e in s.DwExprs)
                        CheckUndefined(e, s);
                if (s.Directive is "org" or "ds")
                {
                    Expression.TryParse(s.DirectiveArg ?? "", out var orgExpr, out _);
                    CheckUndefined(orgExpr, s);
                }
            }

            // Sequential walk: addresses, sizes, overlaps.
            int pc = 0;
            foreach (var s in _stmts)
            {
                if (s.Label is not null)
                {
                    _labels[s.Label] = pc;
                }

                if (s.Directive == "org")
                {
                    int target = OrgTarget(s, pc);
                    if (target < pc)
                        Error(s.Line, s.Column, $".org moves the position counter backwards (from 0x{pc:X4} to 0x{target:X4})");
                    pc = target;
                    continue;
                }

                s.Size = ComputeSize(s);
                if (s.Size > 0)
                {
                    s.Address = pc;
                    if (pc + s.Size > St16Assembler.RamSize)
                    {
                        Error(s.Line, s.Column,
                            $"assembly reaches 0x{(pc + s.Size).ToString("X4")}; the RAM region ends at 0xEFFF");
                        continue;
                    }
                    bool overlapped = false;
                    for (int a = pc; a < pc + s.Size; a++)
                    {
                        if (_writtenLine.TryGetValue(a, out int priorLine))
                        {
                            Error(s.Line, s.Column, $"overlap at 0x{a:X4}: word already written at line {priorLine}");
                            overlapped = true;
                            break;
                        }
                    }
                    if (!overlapped)
                    {
                        for (int a = pc; a < pc + s.Size; a++)
                        {
                            _written.Add(a);
                            _writtenLine[a] = s.Line;
                        }
                        pc += s.Size;
                    }
                }
            }
        }

        private void CheckUndefined(Expression? e, Stmt s)
        {
            if (e is not null && e.HasLabel && !_labels.ContainsKey(e.Label!))
                Error(s.Line, s.Column, $"undefined label '{e.Label}'");
        }

        /// <summary>Evaluate the <c>.org</c> argument (RAM check). Returns the target (0 on error).</summary>
        private int OrgTarget(Stmt s, int currentPc)
        {
            if (!Expression.TryParse(s.DirectiveArg ?? "", out var e, out var err) || !e.IsConstant)
            {
                Error(s.Line, s.Column, err ?? $".org takes a constant address; got '{s.DirectiveArg}'");
                return currentPc;
            }
            if (e.Constant < 0 || e.Constant > St16Assembler.LastAddress)
            {
                Error(s.Line, s.Column, $".org target 0x{e.Constant:X} is beyond the RAM region (0x0000–0xEFFF)");
                return currentPc;
            }
            return (int)e.Constant;
        }

        /// <summary>Word count written by a stmt (org/directives with bad args were reported at parse time).</summary>
        private int ComputeSize(Stmt s)
        {
            if (s.IsInstruction)
                return s.OperandKind == OperandKind.Absolute16 ? 2 : 1;

            switch (s.Directive)
            {
                case "dw":
                    return s.DwExprs?.Count ?? 0;
                case "db":
                    return 1;
                case "ds":
                    if (Expression.TryParse(s.DirectiveArg ?? "", out var e, out _) && e.IsConstant && e.Constant >= 0)
                        return (int)e.Constant;
                    return 0;
                default:
                    return 0;
            }
        }

        // ------------------------------------------------------------------
        // Pass 2: emit words, range-check operands, collect .st16d data.
        // ------------------------------------------------------------------

        private void Pass2()
        {
            int pc = 0;
            foreach (var s in _stmts)
            {
                if (s.Label is not null)
                    _labelSymbols.Add((s.Label, pc));

                if (s.Directive == "org")
                {
                    pc = OrgTarget(s, pc);
                    continue;
                }

                if (s.Size > 0)
                {
                    Emit(s, pc);
                    pc += s.Size;
                }
            }
        }

        private void Emit(Stmt s, int pc)
        {
            if (s.IsInstruction)
            {
                EmitInstruction(s, pc);
                return;
            }

            switch (s.Directive)
            {
                case "dw":
                    int i = 0;
                    foreach (var e in s.DwExprs!)
                    {
                        long v = Eval(e);
                        if (v < 0 || v > 0xFFFF)
                            Error(s.Line, s.Column, $".dw value {v} is out of range (0–65535)");
                        WriteWord(pc + i, v, s);
                        i++;
                    }
                    break;
                case "db":
                {
                    var parts = SplitArgs(s.DirectiveArg ?? "");
                    ushort word = 0;
                    bool ok = true;
                    for (int b = 0; b < parts.Count; b++)
                    {
                        long v = EvalRaw(parts[b], s);
                        if (v < 0 || v > 0xFF)
                        {
                            Error(s.Line, s.Column, $".db byte {v} is out of range (0–255)");
                            ok = false;
                            break;
                        }
                        word = (ushort)(word | (v << (8 * b)));
                    }
                    if (ok)
                        WriteWord(pc, word, s);
                    break;
                }
                case "ds":
                    for (int k = 0; k < s.Size; k++)
                        WriteWord(pc + k, 0, s);
                    break;
            }
        }

        private void EmitInstruction(Stmt s, int pc)
        {
            var op = s.Mnemonic!.Value;
            var info = OpcodeTable.Get(op);
            ushort header = (ushort)((int)op << 8);

            switch (s.OperandKind)
            {
                case OperandKind.Signed8Immediate:
                {
                    long v = EvalOperand(s);
                    if (v < -128 || v > 127)
                    {
                        Error(s.Line, s.Column, $"{info.Mnemonic} operand {v} is out of range (−128…127)");
                        return;
                    }
                    WriteWord(pc, (ushort)(header | (byte)(sbyte)v), s);
                    break;
                }
                case OperandKind.Signed8Relative:
                {
                    long target = EvalOperand(s);
                    if (target < 0 || target > 0xFFFF)
                    {
                        Error(s.Line, s.Column, $"{info.Mnemonic} target {target} is out of range (0–65535)");
                        return;
                    }
                    int offset = (int)target - (pc + 1);
                    if (offset < -127 || offset > 127)
                    {
                        Error(s.Line, s.Column,
                            $"{info.Mnemonic} offset {offset} is out of range (−127…+127; the branch reaches ±127 words from the next instruction)");
                        return;
                    }
                    WriteWord(pc, (ushort)(header | (byte)(sbyte)offset), s);
                    break;
                }
                case OperandKind.Absolute16:
                {
                    long target = EvalOperand(s);
                    if (target < 0 || target > 0xFFFF)
                    {
                        Error(s.Line, s.Column, $"{info.Mnemonic} target {target} is out of range (0–65535)");
                        return;
                    }
                    if (op == Opcode.Call)
                        _callTargets.Add((int)target);
                    WriteWord(pc, header, s);
                    WriteWord(pc + 1, (ushort)target, s);
                    break;
                }
                default: // Ignored — operand byte is reserved 0.
                    WriteWord(pc, header, s);
                    break;
            }
        }

        /// <summary>Evaluate an instruction operand (default 0 when omitted).</summary>
        private long EvalOperand(Stmt s)
            => s.OperandExpr is null ? 0 : Eval(s.OperandExpr);

        private long Eval(Expression e)
            => e.IsConstant ? e.Constant : _labels[e.Label!] + e.Offset;

        private long EvalRaw(string text, Stmt s)
        {
            if (!Expression.TryParse(text, out var e, out _))
                return -1;
            return Eval(e);
        }

        private void WriteWord(int address, long value, Stmt s)
        {
            _words[address] = (ushort)value;
            if (address > _lastWritten)
                _lastWritten = address;
            _lineInfos.Add(new LineInfo(address, s.File, s.Line));
            if (s.IsDataDirective)
                _dataWords.Add(address);
        }

        // ------------------------------------------------------------------
        // Results: words + .st16d.
        // ------------------------------------------------------------------

        private ushort[] BuildWords()
            => _lastWritten < 0
                ? Array.Empty<ushort>()
                : _words[..(int)(_lastWritten + 1)];

        private St16dFile BuildSymbols()
        {
            var symbols = _labelSymbols
                .Select(t => new SymbolInfo(t.Name, t.Address,
                    _callTargets.Contains(t.Address) ? SymbolKind.Function
                    : _dataWords.Contains(t.Address) ? SymbolKind.Data
                    : SymbolKind.Label))
                .ToList();

            var functionEntries = symbols
                .Where(s => s.Kind == SymbolKind.Function)
                .Select(s => s.Address)
                .ToList();

            var functions = symbols
                .Where(s => s.Kind == SymbolKind.Function)
                .OrderBy(s => s.Address)
                .Select(s => new FunctionInfo(s.Name, s.Address, FunctionEnd(s.Address, functionEntries)))
                .ToList();

            var sections = new List<SectionInfo>();
            for (int i = 0; i <= _lastWritten; )
            {
                if (!_written.Contains(i))
                {
                    i++;
                    continue;
                }
                int origin = i;
                while (i <= _lastWritten && _written.Contains(i))
                    i++;
                sections.Add(new SectionInfo(origin, i - origin));
            }

            return new St16dFile
            {
                Sources = _files,
                Lines = _lineInfos.OrderBy(l => l.Address).ToList(),
                Symbols = symbols.OrderBy(s => s.Address).ThenBy(s => s.Name).ToList(),
                Functions = functions,
                Sections = sections,
            };
        }

        /// <summary>Exclusive end of the contiguous instruction run starting at <paramref name="entry"/>.</summary>
        private int FunctionEnd(int entry, List<int> functionEntries)
        {
            int startStmt = _stmts.FindIndex(s => s.Address == entry);
            if (startStmt < 0)
                return entry + 1;

            int end = entry;
            for (int i = startStmt; i < _stmts.Count; i++)
            {
                var t = _stmts[i];
                if (t.Directive == "org")
                    break;
                if (t.Size == 0)
                    continue; // label-only line: keep scanning
                if (!t.IsInstruction)
                    break;
                end += t.Size;
            }

            foreach (var next in functionEntries.Where(a => a > entry))
                end = Math.Min(end, next);

            return Math.Max(end, entry + 1);
        }
    }
}
