namespace ST16.CC;

public enum CTokenType
{
    End,
    Ident,
    IntLit,
    CharLit,
    StringLit,
    LParen, RParen, LBrace, RBrace, LBracket, RBracket,
    Semi, Comma, Colon, Question, Dot, Ellipsis,
    FloatLit,
    PlusPlus, MinusMinus,
    EqEq, NotEq, LtEq, GtEq, LtLt, GtGt,
    PlusEq, MinusEq, StarEq, SlashEq, PercentEq,
    AmpEq, PipeEq, CaretEq,
    LtLtEq, GtGtEq,
    Arrow,
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, Bang, Lt, Gt, Assign,
}

public sealed class CToken
{
    public CToken(CTokenType type, string text, int line, int column, long value = 0, byte[]? bytes = null, bool unsigned = false)
    {
        Type = type; Text = text; Line = line; Column = column; Value = value; Bytes = bytes; Unsigned = unsigned;
    }
    public CTokenType Type { get; }
    public string Text { get; }
    public int Line { get; }
    public int Column { get; }
    public long Value { get; }
    public byte[]? Bytes { get; }
    public bool Unsigned { get; }
    public override string ToString() => $"{Type} '{Text}' @{Line}:{Column}";
}

public static class CKeyword
{
    public static bool Is(CToken t, string keyword) => t.Type == CTokenType.Ident && t.Text == keyword;
}

public sealed class CLexer
{
    private readonly string _src;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public CLexer(string source) { _src = source; }

    public List<CToken> Lex()
    {
        var tokens = new List<CToken>();
        while (true)
        {
            SkipTrivia();
            if (AtEnd)
            {
                tokens.Add(new CToken(CTokenType.End, "", _line, _col));
                return tokens;
            }
            int line = _line, column = _col;
            char c = _src[_pos];
            if (c == '#')
                throw new CCException(new CCDiagnostic(line, column, "preprocessor lines are not supported"));
            if (char.IsLetter(c) || c == '_')
                tokens.Add(LexIdent(line, column));
            else if (char.IsDigit(c))
                tokens.Add(LexNumber(line, column));
            else if (c == '.' && char.IsDigit(Peek(1)))
                tokens.Add(LexFloatLit(line, column));
            else if (c == '\'')
                tokens.Add(LexCharLit(line, column));
            else if (c == '"')
                tokens.Add(LexStringLit(line, column));
            else
                tokens.Add(LexOpOrPunct(line, column));
        }
    }

    private bool AtEnd => _pos >= _src.Length;
    private char Peek(int offset = 0) => _pos + offset < _src.Length ? _src[_pos + offset] : '\0';
    private char Advance() { char c = _src[_pos]; _pos++; if (c == '\n') { _line++; _col = 1; } else { _col++; } return c; }

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            char c = _src[_pos];
            if (char.IsWhiteSpace(c)) { Advance(); continue; }
            if (c == '/' && Peek(1) == '/') { while (!AtEnd && _src[_pos] != '\n') Advance(); continue; }
            if (c == '/' && Peek(1) == '*') { Advance(); Advance(); while (!AtEnd && !(_src[_pos] == '*' && Peek(1) == '/')) Advance(); if (!AtEnd) { Advance(); Advance(); } continue; }
            break;
        }
    }

    private CToken LexIdent(int line, int column)
    {
        int start = _pos;
        while (!AtEnd && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_')) Advance();
        return new CToken(CTokenType.Ident, _src[start.._pos], line, column);
    }

    private CToken LexNumber(int line, int column)
    {
        int start = _pos;
        bool hex = false, octal = false;
        if (Peek() == '0' && (Peek(1) is 'x' or 'X'))
        {
            hex = true; Advance(); Advance();
            while (!AtEnd && IsHexDigit(_src[_pos])) Advance();
            if (_pos == start + 2) throw new CCException(new CCDiagnostic(line, column, "hex literal needs digits"));
        }
        else { octal = Peek() == '0'; while (!AtEnd && char.IsDigit(_src[_pos])) Advance(); }

        if (!hex && !octal && !AtEnd && _src[_pos] == '.' && char.IsDigit(Peek(1)))
        {
            Advance();
            while (!AtEnd && char.IsDigit(_src[_pos])) Advance();
            var text = _src[start.._pos];
            var parts = text.Split('.');
            long intPart = Convert.ToInt64(parts[0], 10);
            long fracPart = 0;
            if (parts.Length > 1 && parts[1].Length > 0) { string frac = parts[1]; if (frac.Length > 2) frac = frac[..2]; fracPart = Convert.ToInt64(frac.PadRight(2, '0'), 10); }
            long fval = intPart * 256 + fracPart;
            if (intPart < -128 || intPart > 127) throw new CCException(new CCDiagnostic(line, column, "float out of Q8.8 range"));
            if (fval > 0x7FFF || fval < -0x8000) throw new CCException(new CCDiagnostic(line, column, "float exceeds Q8.8 range"));
            return new CToken(CTokenType.FloatLit, text, line, column, fval);
        }

        var intText = _src[start.._pos];
        bool unsigned = false;
        if (!AtEnd && (_src[_pos] is 'u' or 'U')) { unsigned = true; Advance(); intText += _src[_pos - 1]; }
        else if (!AtEnd && (_src[_pos] is 'l' or 'L')) { Advance(); throw new CCException(new CCDiagnostic(line, column, "long suffix not supported")); }

        long value = hex ? Convert.ToInt64(intText[2..], 16) : octal && intText.Length > 1 ? Convert.ToInt64(intText, 8) : Convert.ToInt64(intText, 10);
        if (value > 0xFFFF) throw new CCException(new CCDiagnostic(line, column, "integer literal '" + intText + "' exceeds the 16-bit ST16 profile (max 0xFFFF)"));
        return new CToken(CTokenType.IntLit, intText, line, column, value, unsigned: unsigned);
    }

    private CToken LexFloatLit(int line, int column)
    {
        int start = _pos; Advance();
        while (!AtEnd && char.IsDigit(_src[_pos])) Advance();
        var text = _src[start.._pos];
        var parts = text.Split('.');
        long fracPart = 0;
        if (parts.Length > 1 && parts[1].Length > 0) { string frac = parts[1]; if (frac.Length > 2) frac = frac[..2]; fracPart = Convert.ToInt64(frac.PadRight(2, '0'), 10); }
        long value = fracPart;
        if (value > 0x7FFF) throw new CCException(new CCDiagnostic(line, column, "float exceeds Q8.8 range"));
        return new CToken(CTokenType.FloatLit, text, line, column, value);
    }

    private static bool IsHexDigit(char c) => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private CToken LexCharLit(int line, int column)
    {
        int start = _pos; Advance(); // skip opening quote
        if (AtEnd) throw new CCException(new CCDiagnostic(line, column, "unterminated char"));

        int code;
        if (_src[_pos] == '\\')
            code = ReadEscape();
        else
        {
            code = _src[_pos];
            Advance();
        }

        // If the next char isn't the closing quote, there's more than one
        // character between the quotes (e.g. 'ab') → multi-character literal.
        if (AtEnd || _src[_pos] != '\'')
            throw new CCException(new CCDiagnostic(line, column, "multi-character literal"));
        Advance(); // skip closing quote

        if (code > 0xFF) throw new CCException(new CCDiagnostic(line, column, "char literal exceeds one byte"));
        return new CToken(CTokenType.CharLit, _src[start.._pos], line, column, code);
    }

    private int ReadEscape()
    {
        Advance(); // skip backslash
        char c = _src[_pos];
        if (c == 'x' || c == 'X')
        {
            // Hex escape: \x41 — read all consecutive hex digits.
            Advance();
            int value = 0;
            while (!AtEnd && IsHexDigit(_src[_pos]))
            {
                value = (value << 4) | HexVal(_src[_pos]);
                Advance();
            }
            return value;
        }
        int code = c switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', '\\' => '\\', '\'' => '\'', '"' => '"', _ => throw new CCException(new CCDiagnostic(_line, _col, "unknown escape")) };
        Advance();
        return code;
    }

    private static int HexVal(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        throw new CCException(new CCDiagnostic(0, 0, "invalid hex digit in escape"));
    }

    private CToken LexStringLit(int line, int column)
    {
        int start = _pos; Advance();
        var bytes = new List<byte>();
        while (!AtEnd && _src[_pos] != '"') { if (_src[_pos] == '\\') { bytes.Add((byte)ReadEscape()); } else { bytes.Add((byte)_src[_pos]); Advance(); } }
        if (AtEnd) throw new CCException(new CCDiagnostic(line, column, "unterminated string"));
        Advance();
        return new CToken(CTokenType.StringLit, _src[start.._pos], line, column, bytes: bytes.ToArray());
    }

    private CToken LexOpOrPunct(int line, int column)
    {
        char c = _src[_pos]; Advance();
        // Two- or three-character operators first.
        if (c == '<' && _src[_pos] == '<')
        {
            Advance();
            if (_src[_pos] == '=') { Advance(); return new CToken(CTokenType.LtLtEq, "<<=", line, column); }
            return new CToken(CTokenType.LtLt, "<<", line, column);
        }
        if (c == '>' && _src[_pos] == '>')
        {
            Advance();
            if (_src[_pos] == '=') { Advance(); return new CToken(CTokenType.GtGtEq, ">>=", line, column); }
            return new CToken(CTokenType.GtGt, ">>", line, column);
        }
        if (c == '=' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.EqEq, "==", line, column); }
        if (c == '!' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.NotEq, "!=", line, column); }
        if (c == '<' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.LtEq, "<=", line, column); }
        if (c == '>' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.GtEq, ">=", line, column); }
        if (c == '+' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.PlusEq, "+=", line, column); }
        if (c == '-' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.MinusEq, "-=", line, column); }
        if (c == '*' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.StarEq, "*=", line, column); }
        if (c == '/' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.SlashEq, "/=", line, column); }
        if (c == '%' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.PercentEq, "%=", line, column); }
        if (c == '&' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.AmpEq, "&=", line, column); }
        if (c == '|' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.PipeEq, "|=", line, column); }
        if (c == '^' && _src[_pos] == '=') { Advance(); return new CToken(CTokenType.CaretEq, "^=", line, column); }
        if (c == '+' && _src[_pos] == '+') { Advance(); return new CToken(CTokenType.PlusPlus, "++", line, column); }
        if (c == '-' && _src[_pos] == '-') { Advance(); return new CToken(CTokenType.MinusMinus, "--", line, column); }
        if (c == '-' && _src[_pos] == '>') { Advance(); return new CToken(CTokenType.Arrow, "->", line, column); }
        if (c == '.' && _src[_pos] == '.' && Peek(1) == '.') { Advance(); Advance(); return new CToken(CTokenType.Ellipsis, "...", line, column); }

        CTokenType one = c switch { '(' => CTokenType.LParen, ')' => CTokenType.RParen, '{' => CTokenType.LBrace, '}' => CTokenType.RBrace, '[' => CTokenType.LBracket, ']' => CTokenType.RBracket, ';' => CTokenType.Semi, ',' => CTokenType.Comma, ':' => CTokenType.Colon, '?' => CTokenType.Question, '.' => CTokenType.Dot, '+' => CTokenType.Plus, '-' => CTokenType.Minus, '*' => CTokenType.Star, '/' => CTokenType.Slash, '%' => CTokenType.Percent, '&' => CTokenType.Amp, '|' => CTokenType.Pipe, '^' => CTokenType.Caret, '~' => CTokenType.Tilde, '!' => CTokenType.Bang, '<' => CTokenType.Lt, '>' => CTokenType.Gt, '=' => CTokenType.Assign, _ => throw new CCException(new CCDiagnostic(line, column, "unexpected char")) };
        return new CToken(one, c.ToString(), line, column);
    }

    private CCDiagnostic Fail(int line, int column, string message) => new(line, column, message);
}