namespace ST16.Assembler;

/// <summary>
/// ST16 assembler (spec §8). Two-pass: the first pass resolves label addresses
/// (labels defined at instruction/data start addresses); the second pass emits
/// words and performs all range/bounds checks. All diagnostics are collected —
/// the assembler never stops at the first error.
/// </summary>
public sealed class Assembler
{
    /// <summary>Assembles one or more source files in order; the position counter continues across files.</summary>
    public static AssembleResult Assemble(params SourceFile[] files) =>
        new AssemblerEngine(files).Run();

    /// <summary>Convenience: assemble a single source file.</summary>
    public static AssembleResult Assemble(string fileName, string text) =>
        Assemble(new SourceFile(fileName, text));
}

internal sealed class AssemblerEngine
{
    private readonly SourceFile[] _files;
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly List<SourceLine> _lines = new();
    private readonly HashSet<string> _labelNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _labelAddr = new(StringComparer.Ordinal);
    private readonly List<ushort> _words = new();
    private readonly List<(int Addr, int File, int LineNo)> _emitted = new();
    private int _pc;

    public AssemblerEngine(SourceFile[] files) => _files = files;

    public AssembleResult Run()
    {
        ParseAll();
        ComputeLabelAddresses();
        EmitWords();

        bool success = !_diagnostics.Any(d => d.Level == "error");
        return new AssembleResult(
            success,
            success ? _words.ToArray() : Array.Empty<ushort>(),
            success ? BuildDebugFile() : null,
            _diagnostics);
    }

    // ------------------------------------------------------------------
    // Phase A: lex + parse into a line-oriented IR (line/column tracking)
    // ------------------------------------------------------------------

    private void ParseAll()
    {
        for (int f = 0; f < _files.Length; f++)
        {
            string text = _files[f].Text ?? string.Empty;
            string[] rawLines = text.Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < rawLines.Length; i++)
                ParseLine(f, rawLines[i], i + 1);
        }
    }

    private void ParseLine(int fileIndex, string raw, int lineNo)
    {
        var line = new SourceLine { FileIndex = fileIndex, LineNumber = lineNo };
        _lines.Add(line);

        // ';' starts a comment running to end of line (spec §8.1).
        int codeEnd = raw.IndexOf(';');
        string code = codeEnd < 0 ? raw : raw.Substring(0, codeEnd);

        int pos = 0;
        while (pos < code.Length && IsWs(code[pos])) pos++;
        if (pos >= code.Length)
            return; // blank or comment-only line

        int col = pos + 1;

        // Optional label: identifier immediately followed by ':'.
        if (IsIdentStart(code[pos]))
        {
            int start = pos;
            while (pos < code.Length && IsIdentPart(code[pos])) pos++;
            if (pos < code.Length && code[pos] == ':')
            {
                string name = code.Substring(start, pos - start);
                if (!_labelNames.Add(name))
                    Error(lineNo, col, $"duplicate label '{name}'");
                else
                {
                    line.Label = name;
                    line.LabelColumn = col;
                }

                pos++;
                while (pos < code.Length && IsWs(code[pos])) pos++;
                if (pos >= code.Length)
                    return; // label-only line
                col = pos + 1;
            }
            else
            {
                pos = start; // no ':' — the identifier is the mnemonic itself
            }
        }

        ParseItem(line, code, pos, col, lineNo);
    }

    private void ParseItem(SourceLine line, string code, int pos, int col, int lineNo)
    {
        var item = new Item { Column = col };
        line.Item = item;

        if (code[pos] == '.')
        {
            ParseDirective(item, code, pos, col, lineNo);
            return;
        }

        // Mnemonic (case-insensitive, spec §8.1).
        int start = pos;
        while (pos < code.Length && IsIdentPart(code[pos])) pos++;
        if (pos == start)
        {
            Error(lineNo, col, "expected mnemonic or directive");
            return;
        }

        string mnem = code.Substring(start, pos - start);
        if (!Opcodes.TryGet(mnem, out var info))
        {
            Error(lineNo, col, $"unknown mnemonic '{mnem}'");
            return;
        }

        item.Instruction = info;
        while (pos < code.Length && IsWs(code[pos])) pos++;
        if (pos >= code.Length)
        {
            if (info.Operand != Opcodes.OperandKind.None)
                Error(lineNo, col, $"{info.Mnemonic} requires an operand");
            return;
        }

        Expr operand = ParseExpression(code, pos, lineNo, out int end, out int operandCol);
        if (operand != null)
        {
            if (info.Operand == Opcodes.OperandKind.None)
                Error(lineNo, operandCol, $"{info.Mnemonic} takes no operand");
            else
                item.Operand = operand;
        }

        if (end < code.Length)
            ErrorTrailing(lineNo, code, end);
    }

    private void ParseDirective(Item item, string code, int pos, int col, int lineNo)
    {
        int start = pos + 1;
        pos = start;
        while (pos < code.Length && char.IsLetter(code[pos])) pos++;
        string name = code.Substring(start, pos - start).ToLowerInvariant();
        if (name.Length == 0)
        {
            Error(lineNo, col, "malformed directive: expected a name after '.'");
            return;
        }

        while (pos < code.Length && IsWs(code[pos])) pos++;

        switch (name)
        {
            case "org":
            case "ds":
                item.DirectiveName = name;
                if (pos >= code.Length)
                {
                    Error(lineNo, col, $".{name} requires an argument");
                    return;
                }
                Expr single = ParseExpression(code, pos, lineNo, out int end1, out _);
                if (single != null)
                {
                    item.SingleExpression = single;
                    if (end1 < code.Length)
                        ErrorTrailing(lineNo, code, end1);
                }
                return;

            case "dw":
            case "db":
                item.DirectiveName = name;
                if (pos >= code.Length)
                {
                    Error(lineNo, col, $".{name} requires at least one argument");
                    return;
                }

                int count = 0;
                while (pos < code.Length)
                {
                    Expr e = ParseExpression(code, pos, lineNo, out int end, out _);
                    if (e != null)
                    {
                        item.DataExpressions.Add(e);
                        count++;
                    }

                    if (end <= pos) end = pos + 1; // guarantee forward progress
                    if (end < code.Length && code[end] == ',')
                    {
                        pos = end + 1;
                        while (pos < code.Length && IsWs(code[pos])) pos++;
                        if (pos >= code.Length)
                        {
                            Error(lineNo, end + 1, $"trailing ',' in .{name}");
                            break;
                        }
                        continue;
                    }

                    if (end < code.Length)
                        ErrorTrailing(lineNo, code, end);
                    break;
                }

                if (name == "db" && count > 2)
                    Error(lineNo, col, ".db takes one or two bytes");
                return;

            default:
                Error(lineNo, col, $"unknown directive '.{name}'");
                return;
        }
    }

    /// <summary>
    /// Parses an operand expression (spec §8.2): decimal/0x/0b literal, a label,
    /// <c>label ± const</c>, or <c>const ± label</c>. On failure a diagnostic is
    /// recorded and null is returned; <paramref name="end"/> is the furthest
    /// position reached.
    /// </summary>
    private Expr ParseExpression(string code, int pos, int lineNo, out int end, out int operandCol)
    {
        end = pos;
        operandCol = pos + 1;
        if (pos >= code.Length)
        {
            Error(lineNo, operandCol, "expected an operand expression");
            return null;
        }

        char sign = '\0';
        int p = pos;
        if (code[p] == '+' || code[p] == '-')
        {
            sign = code[p];
            p++;
        }

        if (p < code.Length && IsDecDigit(code[p]))
        {
            long? value = TryParseNumber(code, ref p, lineNo, pos + 1, sign);
            if (value == null)
            {
                end = p;
                return null;
            }

            // Optional "± label" (constant + label form, spec §8.2).
            int q = SkipWs(code, p);
            if (q < code.Length && (code[q] == '+' || code[q] == '-'))
            {
                int opPos = q;
                q = SkipWs(code, q + 1);
                if (q < code.Length && IsIdentStart(code[q]))
                {
                    int s = q;
                    while (q < code.Length && IsIdentPart(code[q])) q++;
                    end = q;
                    return Expr.ConstOp(value.Value, code[opPos], code.Substring(s, q - s));
                }

                Error(lineNo, opPos + 1, "expected a label after the operator in the operand");
                end = q;
                return null;
            }

            end = p;
            return Expr.Const(value.Value);
        }

        if (p < code.Length && IsIdentStart(code[p]))
        {
            int s = p;
            while (p < code.Length && IsIdentPart(code[p])) p++;
            string label = code.Substring(s, p - s);

            // Optional "± number" (label + const form, spec §8.2).
            int q = SkipWs(code, p);
            if (q < code.Length && (code[q] == '+' || code[q] == '-'))
            {
                int opPos = q;
                int r = SkipWs(code, q + 1);
                if (r < code.Length && IsDecDigit(code[r]))
                {
                    long? c = TryParseNumber(code, ref r, lineNo, opPos + 1, '\0');
                    if (c == null)
                    {
                        end = r;
                        return null;
                    }

                    end = r;
                    return Expr.Combine(label, code[opPos], c.Value);
                }

                Error(lineNo, opPos + 1, "expected a number after the operator in the operand");
                end = r;
                return null;
            }

            end = p;
            return Expr.Label(label);
        }

        Error(lineNo, operandCol, $"malformed operand '{code.Substring(pos).Trim()}'");
        end = pos + 1;
        return null;
    }

    /// <summary>Parses a (sign-consumed) numeric literal at code[p]: decimal, 0x hex, 0b binary.</summary>
    private long? TryParseNumber(string code, ref int p, int lineNo, int col, char sign)
    {
        long value;
        if (code[p] == '0' && p + 1 < code.Length && (code[p + 1] == 'x' || code[p + 1] == 'X'))
        {
            int start = p + 2;
            int q = start;
            while (q < code.Length && IsHexDigit(code[q])) q++;
            if (q == start)
            {
                Error(lineNo, col, "malformed hex literal: expected digits after '0x'");
                p = start;
                return null;
            }

            if (q - start > 16)
            {
                Error(lineNo, col, "hex literal out of range");
                p = q;
                return null;
            }

            value = Convert.ToInt64(code.Substring(start, q - start), 16);
            p = q;
        }
        else if (code[p] == '0' && p + 1 < code.Length && (code[p + 1] == 'b' || code[p + 1] == 'B'))
        {
            int start = p + 2;
            int q = start;
            while (q < code.Length && (code[q] == '0' || code[q] == '1')) q++;
            if (q == start)
            {
                Error(lineNo, col, "malformed binary literal: expected digits after '0b'");
                p = start;
                return null;
            }

            value = 0;
            for (int i = start; i < q; i++)
            {
                value = value * 2 + (code[i] == '1' ? 1 : 0);
                if (q - start > 16 && value > 0xFFFF)
                    break;
            }

            if (q - start > 16)
            {
                Error(lineNo, col, "binary literal out of range");
                p = q;
                return null;
            }

            p = q;
        }
        else
        {
            int start = p;
            while (p < code.Length && IsDecDigit(code[p])) p++;
            if (!long.TryParse(code.Substring(start, p - start), out value))
            {
                Error(lineNo, col, "decimal literal out of range");
                return null;
            }
        }

        if (sign == '-')
            value = -value;
        return value;
    }

    // ------------------------------------------------------------------
    // Pass 1: label addresses (deferred re-walk for .org/.ds label operands)
    // ------------------------------------------------------------------

    private void ComputeLabelAddresses()
    {
        int passes = 0;
        bool deferred;

        do
        {
            passes++;
            _labelAddr.Clear();
            int pc = 0;
            deferred = false;

            foreach (var line in _lines)
            {
                if (line.Label != null && !_labelAddr.ContainsKey(line.Label))
                    _labelAddr[line.Label] = pc;

                Item item = line.Item;
                if (item is null)
                    continue;

                if (item.IsDirective)
                {
                    switch (item.DirectiveName)
                    {
                        case "org":
                            if (item.SingleExpression is null)
                                break; // operand error already reported at parse time
                            if (!item.SingleExpression.TryEvaluate(_labelAddr, out long org))
                                deferred = true;
                            else
                                pc = (int)org;
                            break;

                        case "dw":
                            pc += item.DataExpressions.Count;
                            break;

                        case "db":
                            pc += 1;
                            break;

                        case "ds":
                            if (item.SingleExpression is null)
                                break; // operand error already reported at parse time
                            if (!item.SingleExpression.TryEvaluate(_labelAddr, out long n))
                                deferred = true;
                            else
                                pc += (int)Math.Min(int.MaxValue, Math.Max(0, n));
                            break;
                    }
                }
                else if (item.Instruction != null)
                {
                    pc += item.Instruction.IsTwoWord ? 2 : 1;
                }
            }
        }
        while (deferred && passes < 16);

        if (deferred && passes >= 16)
            Error(0, 0, "position counter could not be resolved (circular .org/.ds reference)");
    }

    // ------------------------------------------------------------------
    // Pass 2: emit words; range/bounds checks; collect all diagnostics
    // ------------------------------------------------------------------

    private void EmitWords()
    {
        _pc = 0;
        int maxEnd = 0;

        foreach (var line in _lines)
        {
            Item item = line.Item;
            if (item is null)
                continue;

            if (item.IsDirective)
            {
                switch (item.DirectiveName)
                {
                    case "org":
                        long v;
                        if (item.SingleExpression is null)
                            break; // operand error already reported at parse time
                        if (!EvalChecked(item.SingleExpression, line, out v))
                            break;
                        if (v < 0 || v > Opcodes.RamEnd)
                        {
                            Error(line.LineNumber, item.Column, $".org address {v} out of range (0..0xEFFF)");
                            break;
                        }

                        if (v < maxEnd)
                        {
                            Error(line.LineNumber, item.Column,
                                $".org {v} overlaps already-emitted content (position counter is {_pc})");
                            break;
                        }

                        _pc = (int)v;
                        break;

                    case "dw":
                        foreach (Expr e in item.DataExpressions)
                        {
                            long dv;
                            if (!EvalChecked(e, line, out dv))
                                continue;
                            if (dv < 0 || dv > 0xFFFF)
                            {
                                Error(line.LineNumber, item.Column, $".dw value {dv} out of range (0..65535)");
                                continue;
                            }

                            EmitWord((ushort)dv, line, ref maxEnd);
                        }
                        break;

                    case "db":
                        EmitDb(item, line, ref maxEnd);
                        break;

                    case "ds":
                        long n;
                        if (item.SingleExpression is null)
                            break; // operand error already reported at parse time
                        if (!EvalChecked(item.SingleExpression, line, out n))
                            break;
                        if (n < 0)
                        {
                            Error(line.LineNumber, item.Column, $".ds count {n} must be non-negative");
                            break;
                        }

                        if (_pc + n > Opcodes.RamEnd)
                        {
                            Error(line.LineNumber, item.Column,
                                $".ds {n} would exceed the end of RAM (0xEFFF)");
                            break;
                        }

                        for (int k = 0; k < n; k++)
                            EmitWord(0, line, ref maxEnd);
                        break;
                }

                continue;
            }

            var info = item.Instruction;
            if (info is null)
                continue;

            switch (info.Operand)
            {
                case Opcodes.OperandKind.None:
                    EmitWord(Opcodes.MakeWord(info.Opcode, 0), line, ref maxEnd);
                    break;

                case Opcodes.OperandKind.SignedImmediate:
                {
                    long v;
                    if (item.Operand is null)
                        break; // operand error already reported at parse time
                    if (!EvalChecked(item.Operand, line, out v))
                        break;
                    if (v < -128 || v > 127)
                    {
                        Error(line.LineNumber, item.Column, $"{info.Mnemonic} operand {v} out of range (-128..127)");
                        break;
                    }

                    EmitWord(Opcodes.MakeWord(info.Opcode, (int)v), line, ref maxEnd);
                    break;
                }

                case Opcodes.OperandKind.RelativeOffset:
                {
                    long target;
                    if (item.Operand is null)
                        break; // operand error already reported at parse time
                    if (!EvalChecked(item.Operand, line, out target))
                        break;
                    long offset = target - (_pc + 1);
                    if (offset < -128 || offset > 127)
                    {
                        Error(line.LineNumber, item.Column,
                            $"{info.Mnemonic} branch offset {offset} out of range (-128..127)");
                        break;
                    }

                    EmitWord(Opcodes.MakeWord(info.Opcode, (int)offset), line, ref maxEnd);
                    break;
                }

                case Opcodes.OperandKind.Absolute16:
                {
                    long v;
                    if (item.Operand is null)
                        break; // operand error already reported at parse time
                    if (!EvalChecked(item.Operand, line, out v))
                        break;
                    if (v < 0 || v > 0xFFFF)
                    {
                        Error(line.LineNumber, item.Column, $"{info.Mnemonic} operand {v} out of range (0..65535)");
                        break;
                    }

                    EmitWord(Opcodes.MakeWord(info.Opcode, 0), line, ref maxEnd);
                    EmitWord((ushort)v, line, ref maxEnd);
                    break;
                }
            }
        }
    }

    private void EmitDb(Item item, SourceLine line, ref int maxEnd)
    {
        IReadOnlyList<Expr> bytes = item.DataExpressions;
        if (bytes.Count < 1 || bytes.Count > 2)
            return; // already reported at parse time

        long[] vals = new long[2];
        bool ok = true;
        for (int k = 0; k < bytes.Count; k++)
        {
            if (!EvalChecked(bytes[k], line, out vals[k]))
            {
                ok = false;
                continue;
            }

            if (vals[k] < 0 || vals[k] > 0xFF)
            {
                Error(line.LineNumber, item.Column, $".db byte {vals[k]} out of range (0..255)");
                ok = false;
            }
        }

        if (!ok)
            return;

        // First byte is the LOW 8 bits (spec §8.3: .db 0x12, 0x34 → 0x3412).
        int word = bytes.Count == 2 ? (int)(vals[1] << 8 | vals[0]) : (int)vals[0];
        EmitWord((ushort)word, line, ref maxEnd);
    }

    private void EmitWord(ushort word, SourceLine line, ref int maxEnd)
    {
        if (_pc + 1 > Opcodes.RamEnd)
        {
            Error(line.LineNumber, line.Item!.Column, "emission would exceed the end of RAM (0xEFFF)");
            return;
        }

        _words.Add(word);
        _emitted.Add((_pc, line.FileIndex, line.LineNumber));
        _pc++;
        maxEnd = _pc;
    }

    private bool EvalChecked(Expr expr, SourceLine line, out long value)
    {
        if (!expr.TryEvaluate(_labelAddr, out value))
        {
            Error(line.LineNumber, line.Item!.Column, $"undefined label '{expr.LabelName}'");
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------
    // .st16d construction (success only)
    // ------------------------------------------------------------------

    private St16dDebugFile BuildDebugFile()
    {
        var d = new St16dDebugFile();
        d.Origin = _emitted.Count > 0 ? _emitted[0].Addr : 0;

        // Sections: one per contiguous run of emitted words (a .org gap starts a new one).
        int runStart = -1, runLen = 0;
        foreach (var (addr, _, _) in _emitted)
        {
            if (runStart < 0 || addr != runStart + runLen)
            {
                if (runStart >= 0)
                    d.Sections.Add(new St16dSection { Address = runStart, Length = runLen });
                runStart = addr;
                runLen = 1;
            }
            else
            {
                runLen++;
            }
        }

        if (runStart >= 0)
            d.Sections.Add(new St16dSection { Address = runStart, Length = runLen });

        for (int f = 0; f < _files.Length; f++)
            d.Sources.Add(new St16dSource { Name = _files[f].Name, Text = _files[f].Text });

        foreach (var (addr, file, lineNo) in _emitted)
            d.Lines.Add(new St16dLine { File = file, Line = lineNo, Address = addr });

        // Symbols + functions.
        int programEnd = _emitted.Count > 0 ? _emitted[^1].Addr + 1 : d.Origin;
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (line.Label is null || !_labelAddr.TryGetValue(line.Label, out int addr))
                continue;

            d.Symbols.Add(new St16dSymbol { Name = line.Label, Address = addr, Kind = "label" });

            // Function heuristic: the next non-blank, non-label-only, non-directive
            // line after the label is an instruction.
            bool isFunction = false;
            for (int j = i + 1; j < _lines.Count; j++)
            {
                Item next = _lines[j].Item;
                if (next is null)
                    continue; // blank / comment-only / label-only / error line
                if (next.IsDirective)
                    continue; // directive line (.dw/.db/.ds/.org)
                isFunction = true;
                break;
            }

            if (!isFunction)
                continue;

            d.Symbols.Add(new St16dSymbol { Name = line.Label, Address = addr, Kind = "function" });

            int end = programEnd;
            foreach (var l in _lines)
            {
                if (l.Label != null && _labelAddr.TryGetValue(l.Label, out int a) && a > addr && a < end)
                    end = a;
            }

            d.Functions.Add(new St16dFunction { Name = line.Label, Entry = addr, End = end });
        }

        return d;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void Error(int line, int col, string message) =>
        _diagnostics.Add(new Diagnostic("error", message, line, col));

    private void ErrorTrailing(int lineNo, string code, int pos)
    {
        int q = pos;
        while (q < code.Length && IsWs(code[q])) q++;
        if (q < code.Length)
            Error(lineNo, q + 1, $"unexpected characters in operand: '{code.Substring(q).Trim()}'");
    }

    private static bool IsWs(char c) => c == ' ' || c == '\t';

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentPart(char c) => char.IsLetter(c) || IsDecDigit(c) || c == '_';

    private static bool IsDecDigit(char c) => c >= '0' && c <= '9';

    private static bool IsHexDigit(char c) =>
        c >= '0' && c <= '9' || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

    private static int SkipWs(string s, int p)
    {
        while (p < s.Length && IsWs(s[p])) p++;
        return p;
    }

    // ------------------------------------------------------------------
    // Line-oriented IR
    // ------------------------------------------------------------------

    private sealed class SourceLine
    {
        public int FileIndex;
        public int LineNumber; // 1-based
        public string Label;
        public int LabelColumn;
        public Item Item;
    }

    private sealed class Item
    {
        public Opcodes.InstructionInfo Instruction;
        public string DirectiveName; // "org" | "dw" | "db" | "ds"
        public int Column;
        public Expr Operand;              // instruction operand
        public List<Expr> DataExpressions = new(); // .dw / .db
        public Expr SingleExpression;     // .org / .ds
        public bool IsDirective => DirectiveName != null;
    }

    /// <summary>A spec §8.2 operand expression: constant, label, or label ± constant.</summary>
    private sealed class Expr
    {
        private readonly int _kind; // 0 const · 1 label · 2 label±const · 3 const±label
        private readonly long _value;
        private readonly string _label;
        private readonly char _op;

        private Expr(int kind, long value, string label, char op)
        {
            _kind = kind;
            _value = value;
            _label = label;
            _op = op;
        }

        public static Expr Const(long value) => new(0, value, null, '\0');
        public static Expr Label(string name) => new(1, 0, name, '\0');
        public static Expr Combine(string label, char op, long constant) => new(2, constant, label, op);
        public static Expr ConstOp(long constant, char op, string label) => new(3, constant, label, op);

        public string LabelName => _label;

        /// <summary>
        /// Evaluates the expression. Returns false (no diagnostic) when a referenced
        /// label is not registered yet — used by pass 1 to detect deferrable .org/.ds.
        /// </summary>
        public bool TryEvaluate(IReadOnlyDictionary<string, int> labels, out long value)
        {
            switch (_kind)
            {
                case 0:
                    value = _value;
                    return true;

                case 1:
                    if (!labels.TryGetValue(_label, out int lv))
                    {
                        value = 0;
                        return false;
                    }

                    value = lv;
                    return true;

                case 2: // label ± const
                    if (!labels.TryGetValue(_label, out int a))
                    {
                        value = 0;
                        return false;
                    }

                    value = _op == '+' ? a + _value : a - _value;
                    return true;

                default: // const ± label
                    if (!labels.TryGetValue(_label, out int b))
                    {
                        value = 0;
                        return false;
                    }

                    value = _op == '+' ? _value + b : _value - b;
                    return true;
            }
        }
    }
}
