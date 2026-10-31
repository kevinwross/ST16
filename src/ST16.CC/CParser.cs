namespace ST16.CC;

/// <summary>
/// Recursive-descent parser for the ST16 C core subset (plan P4):
/// declarations (local/global scalars + fixed-size arrays, const, initializers),
/// function definitions, full expression precedence, and all core-subset
/// statements (if/else, while, do-while, for, break, continue, return,
/// goto with local labels). Unsupported constructs (structs, floats,
/// variadics, …) are rejected with line-referenced diagnostics.
/// </summary>
public sealed class CParser
{
    private static readonly Dictionary<string, string> UnsupportedKeywords = new()
    {
        ["long"] = "long",
        ["double"] = "double",
        ["float"] = "float",
        ["struct"] = "structs",
        ["union"] = "unions",
        ["switch"] = "switch statements",
        ["case"] = "case labels",
        ["default"] = "default labels",
        ["typedef"] = "typedef",
        ["extern"] = "extern",
        ["static"] = "static",
        ["volatile"] = "volatile",
        ["register"] = "register",
        ["auto"] = "auto",
    };

    private readonly List<CToken> _t;
    private int _i;

    public CParser(List<CToken> tokens)
    {
        _t = tokens;
    }

    private CToken Tok => _t[_i];
    private CToken Next => _t[_i + 1];

    private CToken Advance() => _t[_i++];

    private bool AtEnd => Tok.Type == CTokenType.End;

    private CCDiagnostic Fail(CToken t, string message) => new(t.Line, t.Column, message);

    private void Expect(CTokenType type, string what, CToken at)
    {
        if (Tok.Type != type)
            throw new CCException(Fail(at, $"expected {what} but found '{Tok.Text}'"));
        Advance();
    }

    private bool TryAccept(CTokenType type)
    {
        if (Tok.Type == type)
        {
            Advance();
            return true;
        }
        return false;
    }

    private static bool IsTypeKeyword(CToken t)
        => CKeyword.Is(t, "int") || CKeyword.Is(t, "short") || CKeyword.Is(t, "char")
           || CKeyword.Is(t, "void") || CKeyword.Is(t, "unsigned") || CKeyword.Is(t, "signed")
           || CKeyword.Is(t, "const");

    // -----------------------------------------------------------------------
    // Top level
    // -----------------------------------------------------------------------

    public CProgram ParseProgram()
    {
        var program = new CProgram();
        while (!AtEnd)
        {
            RejectUnsupportedKeyword();
            int headerLine = Tok.Line; // line where the declaration starts
            var baseType = ParseTypeSpecifier();
            var (name, type, paramsInfo) = ParseDeclarator(baseType);
            if (paramsInfo is not null)
            {
                if (Tok.Type == CTokenType.Semi)
                {
                    // Forward declaration (prototype): 'int f(int, int);'
                    Advance();
                    bool hasDef = program.Functions.Any(x => x.Name == name && !x.IsPrototype);
                    bool hasAny = program.Functions.Any(x => x.Name == name);
                    if (!hasDef && !hasAny)
                        program.Functions.Add(new CFuncDef(name, type, paramsInfo,
                            new CBlock { Line = headerLine }, headerLine) { IsPrototype = true });
                    continue;
                }
                var body = ParseBlock(); // expects the '{' itself
                program.Functions.Add(new CFuncDef(name, type, paramsInfo, body, headerLine));
            }
            else
            {
                var declLine = Tok.Line;
                var decls = new List<(string Name, CType Type, CExpr? Init)>();
                var init = ParseOptionalInitializer();
                decls.Add((name, FixInferredArrayLength(type, init, declLine), init));
                while (TryAccept(CTokenType.Comma))
                {
                    var (n2, t2, p2) = ParseDeclarator(baseType);
                    if (p2 is not null)
                        throw new CCException(Fail(Tok, "global declarations cannot have parameter lists"));
                    var init2 = ParseOptionalInitializer();
                    decls.Add((n2, FixInferredArrayLength(t2, init2, declLine), init2));
                }
                Expect(CTokenType.Semi, "';'", Tok);
                foreach (var (n, t, i) in decls)
                    program.Globals.Add(new CGlobalVar(t, n, i, declLine));
            }
        }
        return program;
    }

    /// <summary>
    /// Parses one declarator against a type specifier: prefix pointers, the
    /// identifier, array suffixes, and (for functions) the parameter list.
    /// Returns (name, composed type, params?).
    /// </summary>
    private (string Name, CType Type, List<CParam>? Params) ParseDeclarator(CType baseType)
    {
        // Prefix pointers: int *p / int **pp.
        int ptrs = 0;
        while (Tok.Type == CTokenType.Star)
        {
            Advance();
            ptrs++;
        }

        var nameTok = Tok;
        if (nameTok.Type != CTokenType.Ident)
            throw new CCException(Fail(nameTok, $"expected an identifier after type '{baseType}'"));
        Advance();

        if (UnsupportedKeywords.TryGetValue(nameTok.Text, out var what))
            throw new CCException(Fail(nameTok, $"'{nameTok.Text}' ({what}) is not supported in the ST16 profile"));

        // Suffixes: array dimensions (and function parameters).
        var t = baseType;
        var arrayLens = new List<int>();
        while (Tok.Type == CTokenType.LBracket)
        {
            Advance();
            var len = ParseArrayLength();
            Expect(CTokenType.RBracket, "']'", nameTok);
            arrayLens.Add(len);
        }

        List<CParam>? paramList = null;
        if (Tok.Type == CTokenType.LParen)
        {
            paramList = ParseParams(nameTok);
        }

        // Pointers wrap first, then array suffixes (right-to-left C rule).
        for (int i = 0; i < ptrs; i++)
            t = CType.Pointer(t);
        foreach (var len in arrayLens)
            t = CType.Array(t, len);

        return (nameTok.Text, t, paramList);
    }

    /// <summary>Parses a dimension; <c>[ ]</c> (size inferred from an initializer) is 0.</summary>
    private int ParseArrayLength()
    {
        if (Tok.Type == CTokenType.RBracket)
            return 0; // inferred
        if (Tok.Type != CTokenType.IntLit)
            throw new CCException(Fail(Tok, "array sizes must be constant integer literals (or omitted with an initializer)"));
        var tok = Advance();
        if (tok.Value == 0)
            throw new CCException(Fail(tok, "array size zero is not allowed"));
        return (int)tok.Value;
    }

    /// <summary>Resolves an inferred array length (0) from the initializer, if there is one.</summary>
    private CType FixInferredArrayLength(CType type, CExpr? init, int line)
    {
        if (type.Kind != CTypeKind.Array || type.ArrayLen != 0)
            return type;

        int? inferred = init switch
        {
            CLit lit when lit.Kind == CLitKind.String => lit.Bytes!.Length + 1, // + NUL
            CArrayInit a => a.Items.Count,
            _ => null,
        };
        if (inferred is null)
            throw new CCException(new CCDiagnostic(line, 1,
                "array size must be given when there is no initializer (the ST16 profile does not infer sizes)"));
        return CType.Array(type.Element!, inferred.Value);
    }

    private CType ParseTypeSpecifier()
    {
        bool isConst = false, unsigned = false;
        CType baseType = CType.Int;
        bool baseSeen = false;

        while (true)
        {
            if (CKeyword.Is(Tok, "const"))
            {
                isConst = true;
                Advance();
            }
            else if (CKeyword.Is(Tok, "unsigned"))
            {
                unsigned = true;
                Advance();
            }
            else if (CKeyword.Is(Tok, "signed"))
            {
                Advance(); // 'signed' is a no-op qualifier in this profile
            }
            else if (CKeyword.Is(Tok, "int"))
            {
                baseType = CType.Scalar(CBaseKind.Int, unsigned);
                baseSeen = true;
                Advance();
            }
            else if (CKeyword.Is(Tok, "short"))
            {
                baseType = CType.Scalar(CBaseKind.Short, unsigned);
                baseSeen = true;
                Advance();
            }
            else if (CKeyword.Is(Tok, "char"))
            {
                baseType = CType.Scalar(CBaseKind.Char, unsigned);
                baseSeen = true;
                Advance();
            }
            else if (CKeyword.Is(Tok, "void"))
            {
                Advance();
                return CType.Void;
            }
            else
            {
                break;
            }
        }

        if (!baseSeen)
            baseType = CType.Scalar(CBaseKind.Int, unsigned); // bare 'unsigned'/'const' → int

        return isConst ? baseType.AsConst() : baseType;
    }

    private List<CParam> ParseParams(CToken at)
    {
        Expect(CTokenType.LParen, "'('", at);
        var plist = new List<CParam>();
        if (TryAccept(CTokenType.RParen))
            return plist; // empty ( ) → no parameters
        if (CKeyword.Is(Tok, "void"))
        {
            Advance();
            Expect(CTokenType.RParen, "')' after 'void'", at);
            return plist; // explicit void → no parameters
        }

        while (true)
        {
            var baseType = ParseTypeSpecifier();
            var (name, type, p) = ParseDeclarator(baseType);
            if (p is not null)
                throw new CCException(Fail(at, "function-pointer parameters are not supported in the ST16 profile"));
            plist.Add(new CParam(type, name));
            if (TryAccept(CTokenType.Comma))
                continue;
            break;
        }

        Expect(CTokenType.RParen, "')'", at);
        return plist;
    }

    private CExpr? ParseOptionalInitializer()
    {
        if (Tok.Type == CTokenType.Assign)
        {
            Advance();
            // 'int a[3] = { 1, 2, 3 };' — the brace list follows the '='.
            if (Tok.Type == CTokenType.LBrace)
                return ParseBraceList();
            return ParseAssignment();
        }
        if (Tok.Type == CTokenType.LBrace)
            return ParseBraceList();
        return null;
    }

    /// <summary>Parses a <c>{ expr, expr, … }</c> initializer list.</summary>
    private CExpr ParseBraceList()
    {
        var at = Advance();
        var items = new List<CExpr>();
        if (!TryAccept(CTokenType.RBrace))
        {
            while (true)
            {
                items.Add(ParseAssignment());
                if (!TryAccept(CTokenType.Comma))
                    break;
            }
            Expect(CTokenType.RBrace, "'}' in the initializer list", at);
        }
        return new CArrayInit { Line = at.Line, Items = items };
    }

    private void RejectUnsupportedKeyword()
    {
        if (Tok.Type == CTokenType.Ident && UnsupportedKeywords.TryGetValue(Tok.Text, out var what))
            throw new CCException(Fail(Tok, $"'{Tok.Text}' ({what}) is not supported in the ST16 profile"));
    }

    // -----------------------------------------------------------------------
    // Statements
    // -----------------------------------------------------------------------

    private CBlock ParseBlock()
    {
        int braceLine = Tok.Line;
        Expect(CTokenType.LBrace, "'{'", Tok);
        var block = new CBlock { Line = braceLine };
        while (Tok.Type != CTokenType.RBrace)
        {
            if (AtEnd)
                throw new CCException(Fail(Tok, "unterminated '{' block"));
            block.Statements.Add(ParseStatement());
        }
        Advance(); // '}'
        return block;
    }

    private CStmt ParseStatement()
    {
        RejectUnsupportedKeyword();

        if (Tok.Type == CTokenType.LBrace)
            return ParseBlock();

        // Declaration or expression statement: type keywords start a declaration.
        if (IsTypeKeyword(Tok))
            return ParseDeclarationStatement();

        // Local label: identifier followed by ':'.
        if (Tok.Type == CTokenType.Ident && Next.Type == CTokenType.Colon)
        {
            var labelTok = Advance();
            Advance(); // ':'
            return new CLabel { Line = labelTok.Line, Name = labelTok.Text, Target = ParseStatement() };
        }

        if (CKeyword.Is(Tok, "if"))
        {
            Advance();
            Expect(CTokenType.LParen, "'(' after 'if'", Tok);
            var cond = ParseAssignment();
            Expect(CTokenType.RParen, "')'", Tok);
            var then = ParseStatement();
            CStmt? els = null;
            if (CKeyword.Is(Tok, "else"))
            {
                Advance();
                els = ParseStatement();
            }
            return new CIf { Line = Tok.Line, Cond = cond, Then = then, Else = els };
        }

        if (CKeyword.Is(Tok, "while"))
        {
            var at = Advance();
            Expect(CTokenType.LParen, "'(' after 'while'", at);
            var cond = ParseAssignment();
            Expect(CTokenType.RParen, "')'", at);
            var body = ParseStatement();
            return new CWhile { Line = at.Line, Cond = cond, Body = body };
        }

        if (CKeyword.Is(Tok, "do"))
        {
            var at = Advance();
            var body = ParseStatement();
            if (!CKeyword.Is(Tok, "while"))
                throw new CCException(Fail(Tok, "expected 'while' after the do body"));
            Advance();
            Expect(CTokenType.LParen, "'(' after 'while'", Tok);
            var cond = ParseAssignment();
            Expect(CTokenType.RParen, "')'", Tok);
            Expect(CTokenType.Semi, "';'", Tok);
            return new CDoWhile { Line = at.Line, Body = body, Cond = cond };
        }

        if (CKeyword.Is(Tok, "for"))
        {
            var at = Advance();
            Expect(CTokenType.LParen, "'(' after 'for'", at);

            CStmt? init = null;
            if (Tok.Type != CTokenType.Semi)
            {
                if (IsTypeKeyword(Tok))
                    init = ParseDeclarationStatement();
                else
                {
                    var e = ParseAssignment();
                    Expect(CTokenType.Semi, "';' after the for initializer", at);
                    init = new CExprStmt { Line = at.Line, Expr = e };
                }
            }
            else
            {
                Advance(); // ';'
            }

            CExpr? cond = null;
            if (Tok.Type != CTokenType.Semi)
            {
                cond = ParseAssignment();
            }
            Expect(CTokenType.Semi, "';' after the for condition", at);

            CExpr? step = null;
            if (Tok.Type != CTokenType.RParen)
                step = ParseAssignment();

            Expect(CTokenType.RParen, "')'", at);
            var body = ParseStatement();
            return new CFor { Line = at.Line, Init = init, Cond = cond, Step = step, Body = body };
        }

        if (CKeyword.Is(Tok, "break"))
        {
            var at = Advance();
            Expect(CTokenType.Semi, "';' after 'break'", at);
            return new CBreak { Line = at.Line };
        }

        if (CKeyword.Is(Tok, "continue"))
        {
            var at = Advance();
            Expect(CTokenType.Semi, "';' after 'continue'", at);
            return new CContinue { Line = at.Line };
        }

        if (CKeyword.Is(Tok, "return"))
        {
            var at = Advance();
            CExpr? value = null;
            if (Tok.Type != CTokenType.Semi)
                value = ParseAssignment();
            Expect(CTokenType.Semi, "';' after 'return'", at);
            return new CReturn { Line = at.Line, Value = value };
        }

        if (CKeyword.Is(Tok, "goto"))
        {
            var at = Advance();
            var label = Tok;
            if (label.Type != CTokenType.Ident)
                throw new CCException(Fail(label, "expected a label name after 'goto'"));
            Advance();
            Expect(CTokenType.Semi, "';' after 'goto label'", at);
            return new CGoto { Line = at.Line, Label = label.Text };
        }

        // Expression statement.
        if (Tok.Type == CTokenType.Dot)
            throw new CCException(Fail(Tok, "struct/union member access ('.') is not supported in the ST16 profile"));
        if (Tok.Type == CTokenType.Arrow)
            throw new CCException(Fail(Tok, "struct/union member access ('->') is not supported in the ST16 profile"));

        var expr = ParseAssignment();
        int line = Tok.Line; // the semicolon's line (the statement's line)
        Expect(CTokenType.Semi, "';' after the expression", Tok);
        return new CExprStmt { Line = line, Expr = expr };
    }

    private CStmt ParseDeclarationStatement()
    {
        var at = Tok;
        var baseType = ParseTypeSpecifier();
        var (name, type, plist) = ParseDeclarator(baseType);
        if (plist is not null)
            throw new CCException(Fail(at, "function definitions are not allowed inside functions"));

        var init = ParseOptionalInitializer();
        type = FixInferredArrayLength(type, init, at.Line);
        var decl = new CDecl { Line = at.Line, Base = baseType, Vars = { new CVarDecl(type, name, init) } };
        while (TryAccept(CTokenType.Comma))
        {
            var (n2, t2, p2) = ParseDeclarator(baseType);
            if (p2 is not null)
                throw new CCException(Fail(at, "function definitions are not allowed inside functions"));
            var init2 = ParseOptionalInitializer();
            t2 = FixInferredArrayLength(t2, init2, at.Line);
            decl.Vars.Add(new CVarDecl(t2, n2, init2));
        }
        Expect(CTokenType.Semi, "';'", at);
        return decl;
    }

    // -----------------------------------------------------------------------
    // Expressions (precedence climbing, C order)
    // -----------------------------------------------------------------------

    private CExpr ParseAssignment()
    {
        var left = ParseTernary();

        if (Tok.Type is CTokenType.Assign or CTokenType.PlusEq or CTokenType.MinusEq or CTokenType.StarEq
            or CTokenType.SlashEq or CTokenType.PercentEq or CTokenType.AmpEq or CTokenType.PipeEq
            or CTokenType.CaretEq or CTokenType.LtLtEq or CTokenType.GtGtEq)
        {
            var opTok = Advance();
            var right = ParseAssignment();
            var op = opTok.Type switch
            {
                CTokenType.Assign => BOp.Assign,
                CTokenType.PlusEq => BOp.PlusAssign,
                CTokenType.MinusEq => BOp.MinusAssign,
                CTokenType.StarEq => BOp.MulAssign,
                CTokenType.SlashEq => BOp.DivAssign,
                CTokenType.PercentEq => BOp.ModAssign,
                CTokenType.AmpEq => BOp.AndAssign,
                CTokenType.PipeEq => BOp.OrAssign,
                CTokenType.CaretEq => BOp.XorAssign,
                CTokenType.LtLtEq => BOp.ShlAssign,
                CTokenType.GtGtEq => BOp.ShrAssign,
                _ => BOp.Assign,
            };
            return new CBinary { Line = opTok.Line, Op = op, Left = left, Right = right };
        }

        return left;
    }

    private CExpr ParseTernary()
    {
        var cond = ParseLogicalOr();
        if (TryAccept(CTokenType.Question))
        {
            var then = ParseAssignment();
            Expect(CTokenType.Colon, "':' in the ternary", Tok);
            var els = ParseAssignment();
            return new CTernary { Line = cond.Line, Cond = cond, Then = then, Else = els };
        }
        return cond;
    }

    private CExpr ParseLogicalOr()
    {
        var left = ParseLogicalAnd();
        while (Tok.Type == CTokenType.Pipe && Next.Type == CTokenType.Pipe)
        {
            var at = Advance();
            Advance();
            left = new CBinary { Line = at.Line, Op = BOp.OrOr, Left = left, Right = ParseLogicalAnd() };
        }
        return left;
    }

    private CExpr ParseLogicalAnd()
    {
        var left = ParseBitwiseOr();
        while (Tok.Type == CTokenType.Amp && Next.Type == CTokenType.Amp)
        {
            var at = Advance();
            Advance();
            left = new CBinary { Line = at.Line, Op = BOp.AndAnd, Left = left, Right = ParseBitwiseOr() };
        }
        return left;
    }

    private CExpr ParseBitwiseOr()
    {
        var left = ParseBitwiseXor();
        // A lone '|' is bitwise OR; the pair '||' belongs to the logical level.
        while (Tok.Type == CTokenType.Pipe && Next.Type != CTokenType.Pipe)
        {
            var at = Advance();
            left = new CBinary { Line = at.Line, Op = BOp.Or, Left = left, Right = ParseBitwiseXor() };
        }
        return left;
    }

    private CExpr ParseBitwiseXor()
    {
        var left = ParseBitwiseAnd();
        while (Tok.Type == CTokenType.Caret)
        {
            var at = Advance();
            left = new CBinary { Line = at.Line, Op = BOp.Xor, Left = left, Right = ParseBitwiseAnd() };
        }
        return left;
    }

    private CExpr ParseBitwiseAnd()
    {
        var left = ParseEquality();
        // A lone '&' is bitwise AND; the pair '&&' belongs to the logical level.
        while (Tok.Type == CTokenType.Amp && Next.Type != CTokenType.Amp)
        {
            var at = Advance();
            left = new CBinary { Line = at.Line, Op = BOp.And, Left = left, Right = ParseEquality() };
        }
        return left;
    }

    private CExpr ParseEquality()
    {
        var left = ParseRelational();
        while (Tok.Type is CTokenType.EqEq or CTokenType.NotEq)
        {
            var at = Advance();
            var op = at.Type == CTokenType.EqEq ? BOp.Eq : BOp.Ne;
            left = new CBinary { Line = at.Line, Op = op, Left = left, Right = ParseRelational() };
        }
        return left;
    }

    private CExpr ParseRelational()
    {
        var left = ParseShift();
        while (Tok.Type is CTokenType.Lt or CTokenType.Gt or CTokenType.LtEq or CTokenType.GtEq)
        {
            var at = Advance();
            var op = at.Type switch
            {
                CTokenType.Lt => BOp.Lt,
                CTokenType.Gt => BOp.Gt,
                CTokenType.LtEq => BOp.Le,
                _ => BOp.Ge,
            };
            left = new CBinary { Line = at.Line, Op = op, Left = left, Right = ParseShift() };
        }
        return left;
    }

    private CExpr ParseShift()
    {
        var left = ParseAdditive();
        while (Tok.Type is CTokenType.LtLt or CTokenType.GtGt)
        {
            var at = Advance();
            var op = at.Type == CTokenType.LtLt ? BOp.Shl : BOp.Shr;
            left = new CBinary { Line = at.Line, Op = op, Left = left, Right = ParseAdditive() };
        }
        return left;
    }

    private CExpr ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Tok.Type is CTokenType.Plus or CTokenType.Minus)
        {
            var at = Advance();
            var op = at.Type == CTokenType.Plus ? BOp.Add : BOp.Sub;
            left = new CBinary { Line = at.Line, Op = op, Left = left, Right = ParseMultiplicative() };
        }
        return left;
    }

    private CExpr ParseMultiplicative()
    {
        var left = ParseUnary();
        while (Tok.Type is CTokenType.Star or CTokenType.Slash or CTokenType.Percent)
        {
            var at = Advance();
            var op = at.Type switch
            {
                CTokenType.Star => BOp.Mul,
                CTokenType.Slash => BOp.Div,
                _ => BOp.Mod,
            };
            left = new CBinary { Line = at.Line, Op = op, Left = left, Right = ParseUnary() };
        }
        return left;
    }

    private CExpr ParseUnary()
    {
        if (Tok.Type == CTokenType.PlusPlus)
        {
            var at = Advance();
            return new CUnary { Line = at.Line, Op = UOp.PreInc, Operand = ParseUnary() };
        }
        if (Tok.Type == CTokenType.MinusMinus)
        {
            var at = Advance();
            return new CUnary { Line = at.Line, Op = UOp.PreDec, Operand = ParseUnary() };
        }
        if (CKeyword.Is(Tok, "sizeof"))
        {
            var at = Advance();
            if (Tok.Type == CTokenType.LParen)
            {
                Advance();
                if (IsTypeKeyword(Tok))
                {
                    var type = ParseTypeSpecifier();
                    Expect(CTokenType.RParen, "')' after the sizeof type", at);
                    return new CSizeof { Line = at.Line, Type = type };
                }
                var e = ParseAssignment();
                Expect(CTokenType.RParen, "')'", at);
                return new CSizeof { Line = at.Line, Expr = e };
            }
            return new CSizeof { Line = at.Line, Expr = ParseUnary() };
        }

        switch (Tok.Type)
        {
            case CTokenType.Star:
            {
                var at = Advance();
                return new CUnary { Line = at.Line, Op = UOp.Deref, Operand = ParseUnary() };
            }
            case CTokenType.Amp:
            {
                var at = Advance();
                return new CUnary { Line = at.Line, Op = UOp.AddrOf, Operand = ParseUnary() };
            }
            case CTokenType.Minus:
            {
                var at = Advance();
                return new CUnary { Line = at.Line, Op = UOp.Neg, Operand = ParseUnary() };
            }
            case CTokenType.Bang:
            {
                var at = Advance();
                return new CUnary { Line = at.Line, Op = UOp.Not, Operand = ParseUnary() };
            }
            case CTokenType.Tilde:
            {
                var at = Advance();
                return new CUnary { Line = at.Line, Op = UOp.BitNot, Operand = ParseUnary() };
            }
            case CTokenType.Plus: // unary plus: no-op
            {
                Advance();
                return ParseUnary();
            }
        }

        return ParsePostfix();
    }

    private CExpr ParsePostfix()
    {
        var expr = ParsePrimary();

        while (true)
        {
            if (Tok.Type == CTokenType.LParen)
            {
                var at = Advance();
                var args = new List<CExpr>();
                if (!TryAccept(CTokenType.RParen))
                {
                    while (true)
                    {
                        if (Tok.Type == CTokenType.Ellipsis)
                            throw new CCException(Fail(Tok, "variadic function calls are not supported in the ST16 profile"));
                        args.Add(ParseAssignment());
                        if (!TryAccept(CTokenType.Comma))
                            break;
                    }
                    Expect(CTokenType.RParen, "')'", at);
                }
                expr = new CCall { Line = at.Line, Func = expr, Args = args };
            }
            else if (Tok.Type == CTokenType.LBracket)
            {
                var at = Advance();
                var index = ParseAssignment();
                Expect(CTokenType.RBracket, "']'", at);
                expr = new CIndex { Line = at.Line, Base = expr, Index = index };
            }
            else if (Tok.Type == CTokenType.PlusPlus)
            {
                var at = Advance();
                expr = new CPostfix { Line = at.Line, Operand = expr };
            }
            else if (Tok.Type == CTokenType.MinusMinus)
            {
                var at = Advance();
                expr = new CPostfix { Line = at.Line, Operand = expr, IsDec = true };
            }
            else if (Tok.Type is CTokenType.Dot or CTokenType.Arrow)
            {
                // Member access only makes sense with structs — reject with
                // a profile-specific diagnostic instead of a grammar error.
                throw new CCException(Fail(Tok,
                    $"struct/union member access ('{Tok.Text}') is not supported in the ST16 profile"));
            }
            else
            {
                return expr;
            }
        }
    }

    private CExpr ParsePrimary()
    {
        var tok = Tok;

        switch (tok.Type)
        {
            case CTokenType.IntLit:
                Advance();
                return new CLit { Line = tok.Line, Kind = CLitKind.Int, Value = tok.Value, Unsigned = tok.Unsigned };

            case CTokenType.CharLit:
                Advance();
                return new CLit { Line = tok.Line, Kind = CLitKind.Char, Value = tok.Value };

            case CTokenType.StringLit:
                Advance();
                return new CLit { Line = tok.Line, Kind = CLitKind.String, Bytes = tok.Bytes };

            case CTokenType.Ident:
                if (UnsupportedKeywords.TryGetValue(tok.Text, out var what))
                    throw new CCException(Fail(tok, $"'{tok.Text}' ({what}) is not supported in the ST16 profile"));
                Advance();
                return new CIdent { Line = tok.Line, Name = tok.Text };

            case CTokenType.LParen:
                Advance();
                var inner = ParseAssignment();
                Expect(CTokenType.RParen, "')'", tok);
                return inner;

            case CTokenType.Dot:
                throw new CCException(Fail(tok, "struct/union member access ('.') is not supported in the ST16 profile"));

            case CTokenType.Arrow:
                throw new CCException(Fail(tok, "struct/union member access ('->') is not supported in the ST16 profile"));

            default:
                throw new CCException(Fail(tok, $"unexpected '{tok.Text}' in an expression"));
        }
    }
}
