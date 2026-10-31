namespace ST16.CC;

/// <summary>Scalar base kinds (the ST16 profile: 16-bit int/short, 8-bit-in-a-word char).</summary>
public enum CBaseKind
{
    Void,
    Int,
    Short,
    Char,
}

/// <summary>Shape of a type: scalar, pointer, or fixed-size array.</summary>
public enum CTypeKind
{
    Void,
    Scalar,
    Pointer,
    Array,
}

/// <summary>
/// A C type in the ST16 profile. Scalars are <see cref="CBaseKind"/>;
/// pointers and arrays wrap their element type. <see cref="IsConst"/> is the
/// top-level const qualifier.
/// </summary>
public sealed class CType
{
    private CType(CTypeKind kind, CBaseKind baseKind, bool unsigned, int arrayLen, CType? element, bool isConst)
    {
        Kind = kind;
        Base = baseKind;
        Unsigned = unsigned;
        ArrayLen = arrayLen;
        Element = element;
        IsConst = isConst;
    }

    public CTypeKind Kind { get; }
    public CBaseKind Base { get; }
    public bool Unsigned { get; }
    public int ArrayLen { get; }
    public CType? Element { get; }
    public bool IsConst { get; }

    public static CType Void { get; } = new(CTypeKind.Void, CBaseKind.Void, false, 0, null, false);
    public static CType Int { get; } = new(CTypeKind.Scalar, CBaseKind.Int, false, 0, null, false);
    public static CType Short { get; } = new(CTypeKind.Scalar, CBaseKind.Short, false, 0, null, false);
    public static CType Char { get; } = new(CTypeKind.Scalar, CBaseKind.Char, false, 0, null, false);

    public static CType Scalar(CBaseKind baseKind, bool unsigned)
        => baseKind switch
        {
            CBaseKind.Int => unsigned ? new CType(CTypeKind.Scalar, CBaseKind.Int, true, 0, null, false) : Int,
            CBaseKind.Short => unsigned ? new CType(CTypeKind.Scalar, CBaseKind.Short, true, 0, null, false) : Short,
            CBaseKind.Char => unsigned ? new CType(CTypeKind.Scalar, CBaseKind.Char, true, 0, null, false) : Char,
            _ => Void,
        };

    public static CType Pointer(CType element, bool isConst = false)
        => new(CTypeKind.Pointer, CBaseKind.Void, false, 0, element, isConst);

    public static CType Array(CType element, int len)
        => new(CTypeKind.Array, CBaseKind.Void, false, len, element, false);

    /// <summary>Apply a top-level const qualifier.</summary>
    public CType AsConst()
        => Kind == CTypeKind.Pointer
            ? new CType(CTypeKind.Pointer, Base, Unsigned, ArrayLen, Element, true)
            : Kind == CTypeKind.Array
                ? new CType(CTypeKind.Array, Base, Unsigned, ArrayLen, Element, true)
                : withConst(true);

    private CType withConst(bool isConst)
        => new(Kind, Base, Unsigned, ArrayLen, Element, isConst);

    /// <summary>
    /// C-standard byte size: int/short = 2, char = 1, pointer = 2,
    /// array = len × element. (Layout is uniformly 1 word/object; this is
    /// the value <c>sizeof</c> reports — see implementation.md P3.5.)
    /// </summary>
    public int ByteSize => Kind switch
    {
        CTypeKind.Scalar => Base == CBaseKind.Char ? 1 : 2,
        CTypeKind.Pointer => 2,
        CTypeKind.Array => ArrayLen * Element!.ByteSize,
        _ => 0,
    };

    public override string ToString()
    {
        string s = Kind switch
        {
            CTypeKind.Void => "void",
            CTypeKind.Scalar => (Unsigned ? "unsigned " : "") + Base.ToString().ToLowerInvariant(),
            CTypeKind.Pointer => $"{Element!} *",
            CTypeKind.Array => $"{Element!} [{ArrayLen}]",
            _ => "?",
        };
        if (IsConst)
            s = "const " + s;
        return s;
    }
}

/// <summary>Binary operator kinds (the full core-subset operator set).</summary>
public enum BOp
{
    Add, Sub, Mul, Div, Mod,
    And, Or, Xor, Shl, Shr,
    Eq, Ne, Lt, Gt, Le, Ge,
    AndAnd, OrOr,
    Assign,
    PlusAssign, MinusAssign, MulAssign, DivAssign, ModAssign,
    AndAssign, OrAssign, XorAssign, ShlAssign, ShrAssign,
}

/// <summary>Unary operator kinds.</summary>
public enum UOp
{
    Neg, Not, BitNot, AddrOf, Deref, PreInc, PreDec,
}

// ---------------------------------------------------------------------------
// Statements
// ---------------------------------------------------------------------------

public abstract class CStmt
{
    /// <summary>1-based line in the .c file.</summary>
    public int Line { get; init; }
}

public sealed class CBlock : CStmt
{
    public List<CStmt> Statements { get; init; } = new();
}

/// <summary>A local declaration, possibly with several declarators: <c>int a, b = 3;</c>.</summary>
public sealed class CDecl : CStmt
{
    public CType Base { get; init; } = CType.Void;
    public List<CVarDecl> Vars { get; init; } = new();
}

public sealed record CVarDecl(CType Type, string Name, CExpr? Init);

public sealed class CIf : CStmt
{
    public CExpr Cond { get; init; } = null!;
    public CStmt Then { get; init; } = null!;
    public CStmt? Else { get; init; }
}

public sealed class CWhile : CStmt
{
    public CExpr Cond { get; init; } = null!;
    public CStmt Body { get; init; } = null!;
}

public sealed class CDoWhile : CStmt
{
    public CStmt Body { get; init; } = null!;
    public CExpr Cond { get; init; } = null!;
}

public sealed class CFor : CStmt
{
    /// <summary>First part: a declaration or an expression statement (may be null).</summary>
    public CStmt? Init { get; init; }
    public CExpr? Cond { get; init; }
    public CExpr? Step { get; init; }
    public CStmt Body { get; init; } = null!;
}

public sealed class CBreak : CStmt { }
public sealed class CContinue : CStmt { }

public sealed class CGoto : CStmt
{
    public string Label { get; init; } = string.Empty;
}

/// <summary>A local label and the statement it introduces: <c>label: stmt</c>.</summary>
public sealed class CLabel : CStmt
{
    public string Name { get; init; } = string.Empty;
    public CStmt Target { get; init; } = null!;
}

public sealed class CReturn : CStmt
{
    public CExpr? Value { get; init; }
}

public sealed class CExprStmt : CStmt
{
    public CExpr Expr { get; init; } = null!;
}

// ---------------------------------------------------------------------------
// Expressions
// ---------------------------------------------------------------------------

public abstract class CExpr
{
    /// <summary>1-based line in the .c file.</summary>
    public int Line { get; init; }
}

public enum CLitKind
{
    Int,
    Char,
    String,
}

public sealed class CLit : CExpr
{
    public CLitKind Kind { get; init; }
    public long Value { get; init; }
    public byte[]? Bytes { get; init; }
    public bool Unsigned { get; init; }
}

public sealed class CIdent : CExpr
{
    public string Name { get; init; } = string.Empty;
}

public sealed class CUnary : CExpr
{
    public UOp Op { get; init; }
    public CExpr Operand { get; init; } = null!;
}

/// <summary>Postfix <c>++</c> / <c>--</c>.</summary>
public sealed class CPostfix : CExpr
{
    public CExpr Operand { get; init; } = null!;
    public bool IsDec { get; init; }
}

public sealed class CBinary : CExpr
{
    public BOp Op { get; init; }
    public CExpr Left { get; init; } = null!;
    public CExpr Right { get; init; } = null!;
}

public sealed class CTernary : CExpr
{
    public CExpr Cond { get; init; } = null!;
    public CExpr Then { get; init; } = null!;
    public CExpr Else { get; init; } = null!;
}

public sealed class CCall : CExpr
{
    public CExpr Func { get; init; } = null!;
    public List<CExpr> Args { get; init; } = new();
}

public sealed class CIndex : CExpr
{
    public CExpr Base { get; init; } = null!;
    public CExpr Index { get; init; } = null!;
}

public sealed class CSizeof : CExpr
{
    /// <summary>Exactly one of Expr / Type is non-null.</summary>
    public CExpr? Expr { get; init; }
    public CType? Type { get; init; }
}

/// <summary>A brace-list initializer: <c>{ 1, 2, 3 }</c> (array elements).</summary>
public sealed class CArrayInit : CExpr
{
    public List<CExpr> Items { get; init; } = new();
}

// ---------------------------------------------------------------------------
// Top level
// ---------------------------------------------------------------------------

public sealed record CFuncDef(string Name, CType ReturnType, List<CParam> Params, CBlock Body, int Line)
{
    public bool IsMain => Name == "main";

    /// <summary>True for a forward declaration (<c>int f(int, int);</c>) with no body.</summary>
    public bool IsPrototype { get; init; }
}

public sealed record CParam(CType Type, string Name);

/// <summary>A global variable (optionally initialized).</summary>
public sealed record CGlobalVar(CType Type, string Name, CExpr? Init, int Line);

/// <summary>The parse tree of one .c file.</summary>
public sealed class CProgram
{
    public List<CFuncDef> Functions { get; } = new();
    public List<CGlobalVar> Globals { get; } = new();
}
