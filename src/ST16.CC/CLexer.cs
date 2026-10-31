namespace ST16.CC;

/// <summary>C token kinds (core subset, plan P4).</summary>
public enum CTokenType
{
    End,
    Ident,
    IntLit,
    CharLit,
    StringLit,

    // Punctuation.
    LParen, RParen, LBrace, RBrace, LBracket, RBracket,
    Semi, Comma, Colon, Question, Dot, Ellipsis,

    // Two-character operators.
    PlusPlus, MinusMinus,
    EqEq, NotEq, LtEq, GtEq, LtLt, GtGt,
    PlusEq, MinusEq, StarEq, SlashEq, PercentEq,
    AmpEq, PipeEq, CaretEq,

    // Three-character operators.
    LtLtEq, GtGtEq,
    Arrow,

    // One-character operators.
    Plus, Minus, Star, Slash, Percent,
    Amp, Pipe, Caret, Tilde, Bang, Lt, Gt, Assign,
}

/// <summary>One lexed token with its 1-based line and column (token start).</summary>
public sealed class CToken
{
    public CToken(CTokenType type, string text, int line, int column, long value = 0, byte[]? bytes = null, bool unsigned = false)
    {
        Type = type;
        Text = text;
        Line = line;
        Column = column;
        Value = value;
        Bytes = bytes;
        Unsigned = unsigned;
    }

    public CTokenType Type { get; }
    public string Text { get; }
    public int Line { get; }
    public int Column { get; }

    /// <summary>Numeric value (IntLit, CharLit).</summary>
    public long Value { get; }

    /// <summary>String content for StringLit (without the terminating NUL).</summary>
    public byte[]? Bytes { get; }

    /// <summary>True for a <c>U</c>-suffixed integer literal.</summary>
    public bool Unsigned { get; }

    public override string ToString() => $"{Type} '{Text}' @{Line}:{Column}";
}

/// <summary>Match a keyword against an identifier token (the parser interprets keywords).</summary>
public static class CKeyword
{
    public static bool Is(CToken t, string keyword)
        => t.Type == CTokenType.Ident && t.Text == keyword;
}

/// <summary>
/// Lexer for the ST16 C core subset: identifiers, integer literals
/// (decimal / 0x hex / octal, optional <c>U</c> suffix; <c>long</c> suffix is
/// a line-referenced error), char and string literals with the standard
/// escape set, all core-subset operators, <c>//</c> and <c>/* */</c> comments,
/// and line/column tracking. A preprocessor line (<c>#</c>) is a line-referenced
/// "not supported in the ST16 profile" error.
/// </summary>
public sealed class CLexer
{
    private readonly string _src;
    private int _pos;
    private int _line = 1;
    private int _col = 1;

    public CLexer(string source)
    {
        _src = source;
    }

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
                throw new CCException(new CCDiagnostic(line, column,
                    "preprocessor lines are not supported in the ST16 profile (compile plain C, no #include/#define)"));

            if (char.IsLetter(c) || c == '_')
                tokens.Add(LexIdent(line, column));
            else if (char.IsDigit(c))
                tokens.Add(LexIntLit(line, column));
            else if (c == '\'')
                tokens.Add(LexCharLit(line, column));
            else if (c == '"')
                tokens.Add(LexStringLit(line, column));
            else
                tokens.Add(LexOperator(line, column));
        }
    }

    private bool AtEnd => _pos >= _src.Length;

    private char Peek(int offset = 0) => _pos + offset < _src.Length ? _src[_pos + offset] : '\0';

    private char Advance()
    {
        char c = _src[_pos++];
        if (c == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
        return c;
    }

    private static CCException Fail(int line, int column, string message)
        => new(new CCDiagnostic(line, column, message));

    /// <summary>Skips whitespace and comments.</summary>
    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            char c = _src[_pos];
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                Advance();
            }
            else if (c == '/' && Peek(1) == '/')
            {
                while (!AtEnd && _src[_pos] != '\n')
                    Advance();
            }
            else if (c == '/' && Peek(1) == '*')
            {
                int startLine = _line, startCol = _col;
                Advance();
                Advance();
                while (!AtEnd && !(_src[_pos] == '*' && Peek(1) == '/'))
                    Advance();
                if (AtEnd)
                    throw Fail(startLine, startCol, "unterminated /* comment");
                Advance();
                Advance();
            }
            else
            {
                return;
            }
        }
    }

    private CToken LexIdent(int line, int column)
    {
        int start = _pos;
        while (!AtEnd && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            Advance();
        return new CToken(CTokenType.Ident, _src[start.._pos], line, column);
    }

    private CToken LexIntLit(int line, int column)
    {
        int start = _pos;
        bool hex = false, octal = false;

        if (Peek() == '0' && (Peek(1) is 'x' or 'X'))
        {
            hex = true;
            Advance();
            Advance();
            while (!AtEnd && IsHexDigit(_src[_pos]))
                Advance();
            if (_pos == start + 2)
                throw Fail(line, column, "hexadecimal literal needs at least one hex digit");
        }
        else
        {
            octal = Peek() == '0';
            while (!AtEnd && char.IsDigit(_src[_pos]))
                Advance();
        }

        var text = _src[start.._pos];
        bool unsigned = false;
        if (!AtEnd && (_src[_pos] is 'u' or 'U'))
        {
            unsigned = true;
            Advance();
            text += _src[_pos - 1];
        }
        else if (!AtEnd && (_src[_pos] is 'l' or 'L'))
        {
            Advance();
            throw Fail(line, column,
                $"integer literal '{text}{_src[_pos - 1]}' uses a long suffix — long is not supported in the ST16 profile (16-bit)");
        }

        long value = hex ? Convert.ToInt64(text[2..], 16)
                     : octal && text.Length > 1 ? Convert.ToInt64(text, 8)
                     : Convert.ToInt64(text, 10);
        if (value > 0xFFFF)
            throw Fail(line, column,
                $"integer literal '{text}' exceeds the 16-bit ST16 profile (max 0xFFFF)");
        return new CToken(CTokenType.IntLit, text, line, column, value, unsigned: unsigned);
    }

    private static bool IsHexDigit(char c)
        => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private CToken LexCharLit(int line, int column)
    {
        int start = _pos;
        Advance(); // opening quote
        if (AtEnd)
            throw Fail(line, column, "unterminated character literal");

        int code;
        if (_src[_pos] == '\\')
            code = ReadEscape();
        else
            code = Advance();

        if (!AtEnd && _src[_pos] == '\'')
        {
            Advance();
            return new CToken(CTokenType.CharLit, _src[start.._pos], line, column, code);
        }

        int p = _pos;
        while (!AtEnd && _src[p] != '\'')
            p++;
        throw Fail(line, column,
            $"multi-character literal '{_src[start..(p + 1 < _src.Length ? p + 1 : _src.Length)]}' is not supported in the ST16 profile");
    }

    private CToken LexStringLit(int line, int column)
    {
        Advance(); // opening quote
        var bytes = new List<byte>();
        while (true)
        {
            if (AtEnd)
                throw Fail(line, column, "unterminated string literal");
            if (_src[_pos] == '"')
            {
                Advance();
                break;
            }
            if (_src[_pos] == '\n')
                throw Fail(line, column, "unterminated string literal (newline)");
            if (_src[_pos] == '\\')
            {
                bytes.Add((byte)ReadEscape());
            }
            else
            {
                bytes.Add((byte)Advance());
            }
        }
        return new CToken(CTokenType.StringLit, "", line, column, bytes: bytes.ToArray());
    }

    /// <summary>Reads one escape starting at the backslash; returns its byte value.</summary>
    private int ReadEscape()
    {
        int line = _line, column = _col;
        Advance(); // backslash
        if (AtEnd)
            throw Fail(line, column, "unterminated escape sequence");

        char e = Advance();
        switch (e)
        {
            case 'n': return '\n';
            case 't': return '\t';
            case 'r': return '\r';
            case '0': return 0;
            case '\\': return '\\';
            case '\'': return '\'';
            case '"': return '"';
            case 'a': return '\a';
            case 'b': return '\b';
            case 'f': return '\f';
            case 'v': return '\v';
            case 'x':
            {
                int start = _pos;
                while (!AtEnd && IsHexDigit(_src[_pos]))
                    Advance();
                if (_pos == start)
                    throw Fail(line, column, "empty \\x escape");
                int v = Convert.ToInt32(_src[start.._pos], 16);
                if (v > 0xFF)
                    throw Fail(line, column, $"\\x{_src[start.._pos]} escape exceeds one byte");
                return v;
            }
            default:
                if (char.IsDigit(e))
                {
                    var s = e.ToString();
                    while (!AtEnd && char.IsDigit(_src[_pos]) && s.Length < 3)
                        s += Advance();
                    int v = Convert.ToInt32(s, 8);
                    if (v > 0xFF)
                        throw Fail(line, column, $"octal escape \\{s} exceeds one byte");
                    return v;
                }
                throw Fail(line, column, $"unknown escape sequence '\\{e}'");
        }
    }

    private CToken LexOperator(int line, int column)
    {
        char c = Advance();
        char n = Peek();

        // Three-character first: <<=  >>=  ...
        // (c was consumed; n = Peek() is the next char, Peek(1) the one after.)
        if (c == '.' && n == '.' && Peek(1) == '.')
        {
            Advance();
            Advance();
            return new CToken(CTokenType.Ellipsis, "...", line, column);
        }
        if ((c == '<' || c == '>') && n == c && Peek(1) == '=')
        {
            Advance();
            Advance();
            return new CToken(c == '<' ? CTokenType.LtLtEq : CTokenType.GtGtEq, $"{c}{c}=", line, column);
        }

        // Two-character.
        if (n != '\0')
        {
            var two = (c, n) switch
            {
                ('+', '+') => CTokenType.PlusPlus,
                ('-', '-') => CTokenType.MinusMinus,
                ('=', '=') => CTokenType.EqEq,
                ('!', '=') => CTokenType.NotEq,
                ('<', '=') => CTokenType.LtEq,
                ('>', '=') => CTokenType.GtEq,
                ('<', '<') => CTokenType.LtLt,
                ('>', '>') => CTokenType.GtGt,
                ('+', '=') => CTokenType.PlusEq,
                ('-', '=') => CTokenType.MinusEq,
                ('*', '=') => CTokenType.StarEq,
                ('/', '=') => CTokenType.SlashEq,
                ('%', '=') => CTokenType.PercentEq,
                ('&', '=') => CTokenType.AmpEq,
                ('|', '=') => CTokenType.PipeEq,
                ('^', '=') => CTokenType.CaretEq,
                ('-', '>') => CTokenType.Arrow,
                _ => CTokenType.End,
            };
            if (two != CTokenType.End)
            {
                Advance();
                return new CToken(two, $"{c}{n}", line, column);
            }
        }

        var one = c switch
        {
            '(' => CTokenType.LParen,
            ')' => CTokenType.RParen,
            '{' => CTokenType.LBrace,
            '}' => CTokenType.RBrace,
            '[' => CTokenType.LBracket,
            ']' => CTokenType.RBracket,
            ';' => CTokenType.Semi,
            ',' => CTokenType.Comma,
            ':' => CTokenType.Colon,
            '?' => CTokenType.Question,
            '.' => CTokenType.Dot,
            '+' => CTokenType.Plus,
            '-' => CTokenType.Minus,
            '*' => CTokenType.Star,
            '/' => CTokenType.Slash,
            '%' => CTokenType.Percent,
            '&' => CTokenType.Amp,
            '|' => CTokenType.Pipe,
            '^' => CTokenType.Caret,
            '~' => CTokenType.Tilde,
            '!' => CTokenType.Bang,
            '<' => CTokenType.Lt,
            '>' => CTokenType.Gt,
            '=' => CTokenType.Assign,
            _ => throw Fail(line, column, $"unexpected character '{c}'"),
        };
        return new CToken(one, c.ToString(), line, column);
    }
}
