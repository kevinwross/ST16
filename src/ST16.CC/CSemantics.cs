namespace ST16.CC;

public enum CSymKind
{
    Function,
    Global,
    Param,
    Local,
    Label,
}

/// <summary>A resolved symbol with its frame slot (params/locals) and declaration line.</summary>
public sealed class CSymbol
{
    public string Name { get; init; } = string.Empty;
    public CType Type { get; init; } = CType.Void;
    public int Line { get; init; }
    public CSymKind Kind { get; init; }
    public int Slot { get; init; }
    public CFuncDef? Func { get; init; }
}

/// <summary>
/// Semantic analysis for the ST16 C core subset: scope rules, symbol
/// resolution, type compatibility, const checks, control-flow legality
/// (break/continue/goto), return-value checks, and the "main" contract.
/// Fail-fast with line-referenced diagnostics.
/// </summary>
public sealed class CSemantics
{
    private readonly CProgram _program;
    private readonly Dictionary<string, CSymbol> _globalScope = new();
    private readonly Dictionary<string, CSymbol> _funcScope = new();

    private List<CSymbol> _locals = new();
    private CFuncDef? _currentFunc;
    private int _nextSlot;
    private int _loopDepth;

    public CSemantics(CProgram program)
    {
        _program = program;
    }

    /// <summary>
    /// Runs analysis. Returns the symbol table (functions, globals, and per
    /// function the ordered local/param slot list) for the code generator.
    /// </summary>
    public (Dictionary<string, CSymbol> Functions, Dictionary<string, CSymbol> Globals, Dictionary<string, List<CSymbol>> Locals) Analyze()
    {
        // Functions first so calls (including forward references) resolve.
        // A definition wins over a prototype of the same name; two
        // definitions (or a definition colliding with a global) is an error.
        foreach (var f in _program.Functions)
        {
            if (f.IsPrototype)
            {
                if (_globalScope.ContainsKey(f.Name))
                    continue; // a definition (or earlier prototype) already declared it
                _globalScope[f.Name] = new CSymbol { Name = f.Name, Type = f.ReturnType, Line = f.Line, Kind = CSymKind.Function, Func = f };
                continue;
            }
            if (_globalScope.ContainsKey(f.Name))
            {
                var existing = _globalScope[f.Name];
                if (existing.Func is { IsPrototype: true })
                {
                    // Definition following a prototype: replace it.
                    _globalScope[f.Name] = new CSymbol { Name = f.Name, Type = f.ReturnType, Line = f.Line, Kind = CSymKind.Function, Func = f };
                    continue;
                }
                throw new CCException(new CCDiagnostic(f.Line, 1, $"function '{f.Name}' redeclares a name already in scope"));
            }
            _globalScope[f.Name] = new CSymbol { Name = f.Name, Type = f.ReturnType, Line = f.Line, Kind = CSymKind.Function, Func = f };
        }

        foreach (var g in _program.Globals)
            DeclareGlobal(g);

        foreach (var f in _program.Functions)
            AnalyzeFunction(f);

        // Prototype-only functions have no body and no slots; give them an
        // empty local list so codegen can still emit their call sites (the
        // assembler then reports the undefined symbol cleanly).
        foreach (var f in _program.Functions)
            if (f.IsPrototype && !_localsByFunc.ContainsKey(f.Name))
                _localsByFunc[f.Name] = new List<CSymbol>();

        if (_program.Functions.All(f => f.Name != "main"))
            throw new CCException(new CCDiagnostic(1, 1, "the program has no 'main' function"));

        var funcs = new Dictionary<string, CSymbol>();
        foreach (var (name, sym) in _globalScope)
            if (sym.Kind == CSymKind.Function)
                funcs[name] = sym;
        return (funcs, _globalScope, _localsByFunc);
    }

    private readonly Dictionary<string, List<CSymbol>> _localsByFunc = new();

    // ---------------------------------------------------------------------

    private void DeclareGlobal(CGlobalVar g)
    {
        if (g.Type.IsConst && g.Type.Kind == CTypeKind.Scalar)
        {
            // fine — const globals are allowed
        }
        if (_globalScope.ContainsKey(g.Name))
            throw new CCException(new CCDiagnostic(g.Line, 1, $"global '{g.Name}' is already declared (line {_globalScope[g.Name].Line})"));

        if (g.Type.Kind == CTypeKind.Array)
        {
            // Array initializers and string initializers are handled by codegen.
            if (g.Init is not null)
                CheckConstantExpr(g.Init, "global initializers must be constant expressions (no identifiers)");
        }
        else if (g.Init is not null)
        {
            CheckConstantExpr(g.Init, "global initializers must be constant expressions (no identifiers)");
            CheckAssignable(g.Type, g.Init, g.Line, $"initializing global '{g.Name}'");
        }

        _globalScope[g.Name] = new CSymbol { Name = g.Name, Type = g.Type, Line = g.Line, Kind = CSymKind.Global };
    }

    private void CheckConstantExpr(CExpr e, string what)
    {
        switch (e)
        {
            case CLit:
                break;
            case CArrayInit ai:
                foreach (var item in ai.Items)
                    CheckConstantExpr(item, what);
                break;
            case CUnary u:
                if (u.Op is UOp.Neg or UOp.BitNot)
                    CheckConstantExpr(u.Operand, what);
                else
                    throw new CCException(new CCDiagnostic(e.Line, 1, what));
                break;
            case CBinary b when b.Op is BOp.Add or BOp.Sub or BOp.Mul or BOp.Div or BOp.Mod or BOp.And or BOp.Or or BOp.Xor or BOp.Shl or BOp.Shr:
                CheckConstantExpr(b.Left, what);
                CheckConstantExpr(b.Right, what);
                break;
            case CSizeof:
                break;
            default:
                throw new CCException(new CCDiagnostic(e.Line, 1, what));
        }
    }

    private void AnalyzeFunction(CFuncDef f)
    {
        if (f.IsPrototype)
            return; // no body to analyze; the definition (if any) is analyzed

        _currentFunc = f;
        _funcScope.Clear();
        _locals = new List<CSymbol>();
        _nextSlot = 0;
        _loopDepth = 0;

        if (f.IsMain && f.Params.Count != 0)
            throw new CCException(new CCDiagnostic(f.Line, 1, "'main' must take no parameters in the ST16 profile"));

        foreach (var p in f.Params)
        {
            if (_funcScope.ContainsKey(p.Name))
                throw new CCException(new CCDiagnostic(f.Line, 1, $"parameter '{p.Name}' is already declared"));
            _funcScope[p.Name] = new CSymbol { Name = p.Name, Type = p.Type, Line = f.Line, Kind = CSymKind.Param, Slot = _nextSlot };
            _locals.Add(_funcScope[p.Name]);
            _nextSlot++;
        }

        // Pre-pass: collect all labels so forward gotos resolve (C allows
        // 'goto done;' before the 'done:' label appears).
        foreach (var l in CollectLabels(f.Body))
        {
            var key = "__label__" + l.Name;
            if (_funcScope.ContainsKey(key))
                throw new CCException(new CCDiagnostic(l.Line, 1, $"duplicate label '{l.Name}'"));
            _funcScope[key] = new CSymbol { Name = l.Name, Kind = CSymKind.Label, Line = l.Line };
        }

        AnalyzeBlock(f.Body);
        _localsByFunc[f.Name] = _locals;

        // Non-void functions must end with a return.
        if (f.ReturnType.Kind != CTypeKind.Void)
        {
            var last = LastMeaningfulStmt(f.Body.Statements);
            if (last is not CReturn)
                throw new CCException(new CCDiagnostic(f.Line, 1,
                    $"function '{f.Name}' has a non-void return type but may fall off the end without a return"));
        }
    }

    private static CStmt? LastMeaningfulStmt(List<CStmt> stmts)
    {
        for (int i = stmts.Count - 1; i >= 0; i--)
        {
            var s = stmts[i];
            if (s is CLabel l)
                s = l.Target;
            if (s is not CDecl)
                return s;
        }
        return null;
    }

    private void AnalyzeBlock(CBlock block)
    {
        foreach (var s in block.Statements)
            AnalyzeStatement(s);
    }

    private void AnalyzeStatement(CStmt s)
    {
        switch (s)
        {
            case CBlock b:
                AnalyzeBlock(b);
                break;

            case CDecl d:
                foreach (var v in d.Vars)
                {
                    if (_funcScope.ContainsKey(v.Name))
                        throw new CCException(new CCDiagnostic(d.Line, 1,
                            $"local '{v.Name}' redeclares a name already in scope (line {_funcScope[v.Name].Line})"));
                    if (v.Type.IsConst && v.Init is null && v.Type.Kind != CTypeKind.Array)
                        throw new CCException(new CCDiagnostic(d.Line, 1,
                            $"const local '{v.Name}' must have an initializer"));
                    if (v.Type.Kind == CTypeKind.Array)
                    {
                        if (v.Type.ArrayLen == 0)
                            throw new CCException(new CCDiagnostic(d.Line, 1, "array size must be given"));
                        if (v.Init is CArrayInit a && a.Items.Count > v.Type.ArrayLen)
                            throw new CCException(new CCDiagnostic(d.Line, 1,
                                $"initializer for '{v.Name}' has {a.Items.Count} items but the array has {v.Type.ArrayLen}"));
                    }
                    else if (v.Init is not null)
                    {
                        CheckAssignable(v.Type, v.Init, d.Line, $"initializing '{v.Name}'");
                    }
                    _funcScope[v.Name] = new CSymbol { Name = v.Name, Type = v.Type, Line = d.Line, Kind = CSymKind.Local, Slot = _nextSlot };
                    _locals.Add(_funcScope[v.Name]);
                    _nextSlot++;
                }
                break;

            case CIf c:
                CheckExpr(c.Cond, c.Line, "the 'if' condition");
                AnalyzeStatement(c.Then);
                if (c.Else is not null)
                    AnalyzeStatement(c.Else);
                break;

            case CWhile w:
                _loopDepth++;
                CheckExpr(w.Cond, w.Line, "the 'while' condition");
                AnalyzeStatement(w.Body);
                _loopDepth--;
                break;

            case CDoWhile dw:
                _loopDepth++;
                AnalyzeStatement(dw.Body);
                CheckExpr(dw.Cond, dw.Line, "the 'do-while' condition");
                _loopDepth--;
                break;

            case CFor fo:
                _loopDepth++;
                if (fo.Init is CDecl d2)
                {
                    foreach (var v in d2.Vars)
                    {
                        if (_funcScope.ContainsKey(v.Name))
                            throw new CCException(new CCDiagnostic(fo.Line, 1, $"local '{v.Name}' redeclares a name already in scope"));
                        _funcScope[v.Name] = new CSymbol { Name = v.Name, Type = v.Type, Line = fo.Line, Kind = CSymKind.Local, Slot = _nextSlot };
                        _locals.Add(_funcScope[v.Name]);
                        _nextSlot++;
                    }
                }
                else if (fo.Init is CExprStmt es)
                {
                    CheckExpr(es.Expr, es.Line, "the for initializer");
                }
                if (fo.Cond is not null)
                    CheckExpr(fo.Cond, fo.Line, "the for condition");
                if (fo.Step is not null)
                    CheckExpr(fo.Step, fo.Line, "the for step");
                AnalyzeStatement(fo.Body);
                _loopDepth--;
                break;

            case CBreak:
                if (_loopDepth == 0)
                    throw new CCException(new CCDiagnostic(s.Line, 1, "'break' outside a loop"));
                break;

            case CContinue:
                if (_loopDepth == 0)
                    throw new CCException(new CCDiagnostic(s.Line, 1, "'continue' outside a loop"));
                break;

            case CGoto g:
                if (!_funcScope.ContainsKey("__label__" + g.Label))
                    throw new CCException(new CCDiagnostic(s.Line, 1, $"'goto {g.Label}': the label '{g.Label}' is not declared in this function"));
                break;

            case CLabel l:
                // Already reserved by the label pre-pass.
                AnalyzeStatement(l.Target);
                break;

            case CReturn r:
            {
                var ret = _currentFunc!.ReturnType;
                if (ret.Kind == CTypeKind.Void && r.Value is not null)
                    throw new CCException(new CCDiagnostic(s.Line, 1, "'return' with a value in a void function"));
                if (ret.Kind != CTypeKind.Void)
                {
                    if (r.Value is null)
                        throw new CCException(new CCDiagnostic(s.Line, 1, $"function '{_currentFunc.Name}' must return a value"));
                    CheckAssignable(ret, r.Value, s.Line, "returning from " + _currentFunc.Name);
                }
                // Also check the value expression itself (e.g. a call in
                // 'return f(a, b);' still needs its call checks).
                if (r.Value is not null)
                    CheckExpr(r.Value, s.Line, "the return expression");
                break;
            }

            case CExprStmt es:
                CheckExpr(es.Expr, es.Line, "the expression statement");
                break;
        }
    }

    private void CheckExpr(CExpr e, int line, string what)
    {
        CheckLvalue(e);
    }

    // ---------------------------------------------------------------------
    // Expression checks
    // ---------------------------------------------------------------------

    private void CheckLvalue(CExpr e)
    {
        switch (e)
        {
            case CIdent i:
                ResolveName(i.Name, i.Line);
                break;
            case CUnary u:
                switch (u.Op)
                {
                    case UOp.Deref:
                    case UOp.PreInc:
                    case UOp.PreDec:
                        CheckLvalue(u.Operand);
                        break;
                    default:
                        CheckLvalue(u.Operand);
                        break;
                }
                break;
            case CPostfix p:
                CheckLvalue(p.Operand);
                break;
            case CIndex ix:
                CheckLvalue(ix.Base);
                CheckLvalue(ix.Index);
                break;
            case CBinary b:
                if (IsAssignment(b.Op))
                {
                    CheckLvalue(b.Left);
                    CheckLvalue(b.Right);
                    if (b.Left is CIdent li && _funcScope.TryGetValue(li.Name, out var s) && s.Type.IsConst)
                        throw new CCException(new CCDiagnostic(b.Line, 1, $"assignment to const object '{li.Name}'"));
                    if (LvalueType(b.Left) is { } lhsType)
                        CheckAssignable(lhsType, b.Right, b.Line, $"assigning to '{b.Left}'");
                }
                else
                {
                    CheckLvalue(b.Left);
                    CheckLvalue(b.Right);
                }
                break;
            case CCall c:
                if (c.Func is CIdent fi)
                {
                    // Built-ins (plan P3.5): putchar(c) writes port 0xF000,
                    // getchar() reads port 0xF001.
                    if (fi.Name == "putchar" || fi.Name == "getchar")
                    {
                        int expected = fi.Name == "putchar" ? 1 : 0;
                        if (expected != c.Args.Count)
                            throw new CCException(new CCDiagnostic(c.Line, 1,
                                $"'{fi.Name}' expects {expected} argument{(expected == 1 ? "" : "s")}, got {c.Args.Count}"));
                        foreach (var a in c.Args)
                            CheckLvalue(a);
                        break;
                    }
                    if (!_globalScope.TryGetValue(fi.Name, out var fsym) || fsym.Kind != CSymKind.Function || fsym.Func is null)
                        throw new CCException(new CCDiagnostic(c.Line, 1, $"'{fi.Name}' is not a function"));
                    int argc = fsym.Func.Params.Count;
                    if (argc != c.Args.Count)
                        throw new CCException(new CCDiagnostic(c.Line, 1,
                            $"'{fi.Name}' expects {argc} argument{(argc == 1 ? "" : "s")}, got {c.Args.Count}"));
                }
                else
                {
                    throw new CCException(new CCDiagnostic(c.Line, 1, "only direct function calls are supported (no function pointers)"));
                }
                foreach (var a in c.Args)
                    CheckLvalue(a);
                break;
            case CSizeof so:
                if (so.Expr is not null)
                    CheckLvalue(so.Expr);
                else if (so.Type is not null && so.Type.Kind == CTypeKind.Void)
                    throw new CCException(new CCDiagnostic(so.Line, 1, "'sizeof(void)' is not allowed"));
                break;
            case CTernary t:
                CheckLvalue(t.Cond);
                CheckLvalue(t.Then);
                CheckLvalue(t.Else);
                break;
            case CLit:
                break;
            default:
                break;
        }
    }

    private static List<CLabel> CollectLabels(CStmt s)
    {
        var labels = new List<CLabel>();
        CollectLabelsInto(s, labels);
        return labels;
    }

    private static void CollectLabelsInto(CStmt s, List<CLabel> labels)
    {
        switch (s)
        {
            case CBlock b:
                foreach (var st in b.Statements)
                    CollectLabelsInto(st, labels);
                break;
            case CIf c:
                CollectLabelsInto(c.Then, labels);
                if (c.Else is not null)
                    CollectLabelsInto(c.Else, labels);
                break;
            case CWhile w:
                CollectLabelsInto(w.Body, labels);
                break;
            case CDoWhile dw:
                CollectLabelsInto(dw.Body, labels);
                break;
            case CFor fo:
                CollectLabelsInto(fo.Body, labels);
                break;
            case CLabel l:
                labels.Add(l);
                CollectLabelsInto(l.Target, labels);
                break;
        }
    }

    private void ResolveName(string name, int line)
    {
        if (_funcScope.TryGetValue(name, out var s))
        {
            if (s.Kind == CSymKind.Label)
                throw new CCException(new CCDiagnostic(line, 1, $"'{name}' is a label, not a value"));
            return;
        }
        if (_globalScope.TryGetValue(name, out var g))
        {
            if (g.Kind == CSymKind.Function)
                throw new CCException(new CCDiagnostic(line, 1, $"'{name}' is a function; did you mean to call it?"));
            return;
        }
        throw new CCException(new CCDiagnostic(line, 1, $"'{name}' is not declared"));
    }

    private static bool IsAssignment(BOp op)
        => op is BOp.Assign or BOp.PlusAssign or BOp.MinusAssign or BOp.MulAssign or BOp.DivAssign
            or BOp.ModAssign or BOp.AndAssign or BOp.OrAssign or BOp.XorAssign or BOp.ShlAssign or BOp.ShrAssign;

    /// <summary>The type denoted by an lvalue expression (null if unknown).</summary>
    private CType? LvalueType(CExpr e)
    {
        switch (e)
        {
            case CIdent id:
                if (_funcScope.TryGetValue(id.Name, out var l) && l.Kind == CSymKind.Local)
                    return l.Type;
                if (_globalScope.TryGetValue(id.Name, out var g))
                    return g.Type;
                return null;
            case CIndex ix:
                return LvalueType(ix.Base) is { } bt
                    ? (bt.Kind is CTypeKind.Pointer or CTypeKind.Array ? bt.Element! : null)
                    : null;
            case CUnary u when u.Op == UOp.Deref:
                return LvalueType(u.Operand) is { } pt && pt.Kind == CTypeKind.Pointer ? pt.Element! : null;
            default:
                return null;
        }
    }

    private void CheckAssignable(CType target, CExpr source, int line, string what)
    {
        // The ST16 profile is permissive about integer interconversion (16-bit
        // int/short, char in the low byte, pointers are words). v1 does not
        // warn on implicit integer/pointer interconversion.
        //
        // One exception: an integer literal above 0x7FFF has the natural
        // type 'unsigned int' and cannot be stored in a signed 16-bit
        // target — that would silently wrap.
        if (source is CLit { Kind: CLitKind.Int, Value: > 0x7FFF } && !target.Unsigned)
            throw new CCException(new CCDiagnostic(line, 1,
                $"constant {((CLit)source).Value} does not fit in type '{FormatType(target)}' (signed 16-bit values end at 32767)"));
        _ = what;
    }

    private static string FormatType(CType t) => t.Kind switch
    {
        CTypeKind.Scalar => (t.Unsigned ? "unsigned " : "") + t.Base.ToString().ToLowerInvariant(),
        CTypeKind.Pointer => t.Element is not null ? $"{FormatType(t.Element)} *" : "pointer",
        CTypeKind.Array => $"array of {FormatType(t.Element!)}",
        _ => "void",
    };
}
