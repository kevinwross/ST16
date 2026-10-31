namespace ST16.CC;

public partial class CCodegen
{
    // ---------------------------------------------------------------------
    // Expressions — each leaves exactly one value on the (managed) stack.
    // ---------------------------------------------------------------------

    private void Emit(CExpr e)
    {
        switch (e)
        {
            case CLit lit:
                EmitLit(lit);
                break;
            case CIdent id:
                EmitIdent(id);
                break;
            case CIndex ix:
                EmitIndex(ix);
                break;
            case CUnary u:
                EmitUnary(u);
                break;
            case CPostfix p:
                EmitPostfix(p);
                break;
            case CBinary b:
                EmitBinary(b);
                break;
            case CTernary t:
                EmitTernary(t);
                break;
            case CCall c:
                EmitCall(c);
                break;
            case CSizeof so:
                Mark(so.Line);
                PushIntScalar(so.Type is not null ? so.Type.ByteSize : InferType(so.Expr!).ByteSize);
                break;
            default:
                throw new CCException(new CCDiagnostic(e.Line, 1, "unsupported expression form in code generation"));
        }
    }

    private void EmitLit(CLit lit)
    {
        Mark(lit.Line);
        switch (lit.Kind)
        {
            case CLitKind.Int:
            case CLitKind.Char:
                PushIntScalar(lit.Value);
                break;
            case CLitKind.String:
                Push16(_stringAddr[lit]);
                break;
        }
    }

    private void PushIntScalar(long v)
    {
        if (v >= -128 && v <= 127)
            _st.Push($"PUSH {(int)v}");
        else
            _st.Push($"PUSH16 0x{(int)(v & 0xFFFF):X4}");
    }

    private void Push16(int value)
    {
        _st.Push($"PUSH16 0x{value & 0xFFFF:X4}");
    }

    // ---------------------------------------------------------------------
    // Identifiers and indexing
    // ---------------------------------------------------------------------

    private void EmitIdent(CIdent id)
    {
        Mark(id.Line);
        if (FindLocal(id.Name) is { } local)
        {
            if (local.Type.Kind == CTypeKind.Array)
            {
                // Arrays decay to their address in value position.
                AddressOfSlot(local.Slot);
            }
            else
            {
                LoadFromSlot(local.Slot, local.Type);
            }
            return;
        }
        if (_globals.TryGetValue(id.Name, out var g) && g.Kind is CSymKind.Global)
        {
            if (g.Type.Kind == CTypeKind.Array)
            {
                Push16(_globalAddrs[id.Name]);
            }
            else
            {
                Push16(_globalAddrs[id.Name]);
                _st.Load();
                AdjustCharLoad(g.Type);
            }
            return;
        }
        throw new CCException(new CCDiagnostic(id.Line, 1, $"'{id.Name}' is not a variable in code generation"));
    }

    /// <summary>Leaves the address of a variable (local slot or global) on top.</summary>
    private void EmitIdentAddr(CIdent id)
    {
        if (FindLocal(id.Name) is { } local)
        {
            AddressOfSlot(local.Slot);
            return;
        }
        if (_globals.TryGetValue(id.Name, out var g) && g.Kind is CSymKind.Global)
        {
            Push16(_globalAddrs[id.Name]);
            return;
        }
        throw new CCException(new CCDiagnostic(id.Line, 1, $"'{id.Name}' is not a variable in code generation"));
    }

    private void EmitIndex(CIndex ix)
    {
        Mark(ix.Line);
        EmitBaseAddr(ix.Base);
        Emit(ix.Index);
        _st.Op("ADD");
        _st.Load();
        AdjustCharLoad(InferType(ix));
    }

    /// <summary>
    /// Leaves the address denoted by an array/pointer expression on top
    /// (no value load). Handles multi-dimensional arrays: <c>a[i][j]</c>
    /// uses <c>a[i]</c> as a base.
    /// </summary>
    private void EmitBaseAddr(CExpr baseExpr)
    {
        if (baseExpr is CIndex inner)
        {
            EmitBaseAddr(inner.Base);
            Emit(inner.Index);
            _st.Op("ADD");
            return;
        }
        if (InferType(baseExpr).Kind == CTypeKind.Array && baseExpr is CIdent id)
        {
            EmitIdentAddr(id);
            return;
        }
        Emit(baseExpr); // a pointer value is the address itself
    }

    // ---------------------------------------------------------------------
    // Unary and postfix
    // ---------------------------------------------------------------------

    private void EmitUnary(CUnary u)
    {
        switch (u.Op)
        {
            case UOp.Neg:
                Emit(u.Operand);
                Mark(u.Line);
                _st.Unop("NEG");
                break;

            case UOp.BitNot:
                Emit(u.Operand);
                Mark(u.Line);
                _st.Unop("NOT");
                break;

            case UOp.Not:
            {
                string nz = NewLabel();
                string end = NewLabel();
                Emit(u.Operand);
                Mark(u.Line);
                _st.JNZ(nz);
                PushIntScalar(1);
                _lines.Add($"JMP {end}");
                EmitLabel(nz);
                PushIntScalar(0);
                EmitLabel(end);
                break;
            }

            case UOp.AddrOf:
                EmitAddrOf(u.Operand, u.Line);
                break;

            case UOp.Deref:
            {
                Emit(u.Operand);
                Mark(u.Line);
                _st.Load();
                AdjustCharLoad(InferType(u));
                break;
            }

            case UOp.PreInc:
            case UOp.PreDec:
            {
                bool dec = u.Op == UOp.PreDec;
                Mark(u.Line);
                EmitAddrOf(u.Operand, u.Line);  // [a]
                _st.Dup();                     // [a, a]
                _st.Load();                    // [a, v]
                PushIntScalar(dec ? -1 : 1);   // [a, v, d]
                _st.Op(dec ? "SUB" : "ADD");   // [a, new]
                _st.Swap();                    // [new, a]
                _st.Store();                   // [new]
                break;
            }
        }
    }

    /// <summary>
    /// Leaves the address of an lvalue: a variable, an array element
    /// (<c>&amp;a[i]</c>), or a dereferenced pointer (<c>&amp;*p</c>).
    /// Also used by <c>++</c>/<c>--</c>.
    /// </summary>
    private void EmitAddrOf(CExpr e, int line)
    {
        switch (e)
        {
            case CIdent id:
                Mark(line);
                EmitIdentAddr(id);
                break;
            case CIndex ix:
                Mark(line);
                EmitBaseAddr(ix.Base);
                Emit(ix.Index);
                _st.Op("ADD");
                break;
            case CUnary u when u.Op == UOp.Deref:
                Mark(line);
                Emit(u.Operand); // the pointer value is the address
                break;
            default:
                throw new CCException(new CCDiagnostic(line, 1,
                    "'&', '++' and '--' apply only to variables, array elements, and pointers"));
        }
    }

    /// <summary>Postfix <c>++</c>/<c>--</c>: the expression yields the OLD value.</summary>
    private void EmitPostfix(CPostfix p)
    {
        Mark(p.Line);
        EmitAddrOf(p.Operand, p.Line);          // [a]
        _st.Dup();                             // [a, a]
        _st.Load();                            // [a, v]
        _st.Swap();                            // [v, a]
        _st.Dup();                             // [v, a, a]
        _st.Load();                            // [v, a, v]
        // SUB/ADD take the top as the right operand (wa − wb / wa + wb), so
        // the delta is pushed positive and the opcode carries the sign:
        // v++ = PUSH 1, ADD (v + 1); v-- = PUSH 1, SUB (v − 1).
        PushIntScalar(1);                      // [v, a, v, 1]
        _st.Op(p.IsDec ? "SUB" : "ADD");       // [v, a, new]
        _st.Swap();                            // [v, new, a]
        _st.Store();                           // [v]
    }

    // ---------------------------------------------------------------------
    // Binary operators
    // ---------------------------------------------------------------------

    private void EmitBinary(CBinary b)
    {
        Mark(b.Line);
        switch (b.Op)
        {
            case BOp.Add:
            case BOp.Sub:
            case BOp.Mul:
            case BOp.And:
            case BOp.Or:
            case BOp.Xor:
                Emit(b.Left);
                Emit(b.Right);
                _st.Op(BinaryMnemonic(b.Op));
                break;

            case BOp.Div:
            case BOp.Mod:
            {
                // The VM's DIV/MOD are signed-only; if either operand is
                // unsigned, values in 0x8000..0xFFFF would wrap to negative
                // — compute with a software loop instead (quotient kept in
                // the scratch cell; [u, v] → [r, v] + q in the cell).
                bool unsignedOp = InferType(b.Left).Unsigned
                    || InferType(b.Right).Unsigned;
                Emit(b.Left);
                Emit(b.Right);
                if (!unsignedOp)
                {
                    _st.Op(BinaryMnemonic(b.Op));
                    break;
                }
                EmitUnsignedDivMod(b.Op == BOp.Div);
                break;
            }

            // SLL/SLR (spec §5.2): value on top, count second — after L; R
            // the count is on top, so SWAP before the shift.
            case BOp.Shl:
                Emit(b.Left);
                Emit(b.Right);
                _st.Swap();
                _st.Op("SLL");
                break;

            case BOp.Shr:
                Emit(b.Left);
                Emit(b.Right);
                _st.Swap();
                _st.Op("SLR");
                break;

            case BOp.Eq:
            case BOp.Ne:
            case BOp.Lt:
            case BOp.Gt:
            case BOp.Le:
            case BOp.Ge:
                Emit(b.Left);
                Emit(b.Right);
                bool u = InferType(b.Left).Unsigned || InferType(b.Right).Unsigned;
                _st.Op(CompareMnemonic(b.Op, u));
                break;

            case BOp.AndAnd:
            {
                // (a && b): JZ/JNZ pop the condition unconditionally.
                string t = NewLabel();
                string nz = NewLabel();
                string end = NewLabel();
                Emit(b.Left);
                _st.JNZ(t);
                PushIntScalar(0);
                _lines.Add($"JMP {end}");
                EmitLabel(t);
                Emit(b.Right);
                _st.JNZ(nz);
                PushIntScalar(0);
                _lines.Add($"JMP {end}");
                EmitLabel(nz);
                PushIntScalar(1);
                EmitLabel(end);
                break;
            }

            case BOp.OrOr:
            {
                string f = NewLabel();
                string f2 = NewLabel();
                string end = NewLabel();
                Emit(b.Left);
                _st.JZ(f);
                PushIntScalar(1);
                _lines.Add($"JMP {end}");
                EmitLabel(f);
                Emit(b.Right);
                _st.JZ(f2);
                PushIntScalar(1);
                _lines.Add($"JMP {end}");
                EmitLabel(f2);
                PushIntScalar(0);
                EmitLabel(end);
                break;
            }

            case BOp.Assign:
            {
                // x = e:  e; DUP; addr(x); STORE  → [e]
                Emit(b.Right);
                CharMaskIf(InferType(b.Left));
                _st.Dup();
                EmitAddrOf(b.Left, b.Line);
                _st.Store();
                break;
            }

            case BOp.PlusAssign:
            case BOp.MinusAssign:
            case BOp.MulAssign:
            case BOp.DivAssign:
            case BOp.ModAssign:
            case BOp.AndAssign:
            case BOp.OrAssign:
            case BOp.XorAssign:
            case BOp.ShlAssign:
            case BOp.ShrAssign:
            {
                // x op= e:  addr(x); load; e; op; DUP; addr(x); STORE → [result]
                EmitAddrOf(b.Left, b.Line);
                _st.Load();
                Emit(b.Right);
                if (b.Op is BOp.DivAssign or BOp.ModAssign
                    && InferType(b.Left).Unsigned)
                {
                    // unsigned lvalue: the VM division is signed-only.
                    EmitUnsignedDivMod(b.Op == BOp.DivAssign);
                }
                else
                {
                    // SLL/SLR want the value on top and the count second.
                    if (b.Op is BOp.ShlAssign or BOp.ShrAssign)
                        _st.Swap();
                    _st.Op(CompoundMnemonic(b.Op));
                }
                _st.Dup();
                EmitAddrOf(b.Left, b.Line);
                _st.Store();
                break;
            }
        }
    }

    /// <summary>
    /// Software unsigned division/modulo. Entry: [u, v] (u second, v on
    /// top). The VM's DIV/MOD are signed-only, so for values in
    /// 0x8000..0xFFFF a subtraction loop computes the quotient (kept in
    /// scratch cell 1) and the remainder: while (u >= v) { u -= v; q++; }.
    ///
    /// VM facts that shape the sequence: comparisons pop BOTH operands and
    /// compute second &gt;= top; STORE consumes (second = value, top =
    /// address); JZ pops the condition. The divisor is parked in scratch
    /// cell 2 so the comparison (which consumes its operands) can be
    /// re-established each iteration: the per-iteration compare idiom
    /// [u] → LOAD v; SWAP; DUP; ROT → [u, u, v] → UGE → [u, c] leaves the
    /// bottom u copy below the condition.
    ///
    /// Exit: [q] for division, [r] for modulo (single value on top).
    /// (v == 0 loops forever — C division by zero is UB.)
    /// </summary>
    private void EmitUnsignedDivMod(bool quotient)
    {
        string top = NewLabel();
        string done = NewLabel();
        string qcell = $"0x{_scratchAddr:X4}";
        string vcell = $"0x{_scratchAddr2:X4}";

        // q = 0; park the divisor.
        _st.Push("PUSH 0");              // [u, v, 0]
        _st.Push($"PUSH16 {qcell}");     // [u, v, 0, aq]
        _st.Store();                      // [u, v]
        _st.Push($"PUSH16 {vcell}");     // [u, v, av]
        _st.Store();                      // [u]

        EmitLabel(top);
        // Compare u >= v without losing u: build [u, u, v] (top two:
        // second = u, top = v), so UGE (second >= top) computes u >= v and
        // leaves the bottom u copy below the condition.
        _st.Push($"PUSH16 {vcell}");     // [u, av]
        _st.Load();                       // [u, v]
        _st.Swap();                       // [v, u]
        _st.Dup();                        // [v, u, u]
        _st.Unop("ROT");                  // [u, u, v]
        _st.Op("UGE");                    // [u, c]
        _st.JZ(done);                     // [u]
        _st.Push($"PUSH16 {vcell}");     // [u, av]
        _st.Load();                       // [u, v]
        _st.Op("SUB");                     // [u']  (u − v)
        // q = q + 1 (q in the scratch cell)
        _st.Push($"PUSH16 {qcell}");     // [u', aq]
        _st.Load();                       // [u', q]
        _st.Push("PUSH 1");               // [u', q, 1]
        _st.Op("ADD");                    // [u', q+1]
        _st.Push($"PUSH16 {qcell}");     // [u', q+1, aq]
        _st.Store();                      // [u']
        _lines.Add($"JMP {top}");

        EmitLabel(done);
        // The JZ exit leaves [u] (u = remainder) + reload v and q.
        _st.Push($"PUSH16 {vcell}");     // [u, av]
        _st.Load();                       // [u, v]
        _st.Push($"PUSH16 {qcell}");     // [u, v, aq]
        _st.Load();                       // [u, v, q]
        _st.Unop("ROT");                  // [v, q, u]
        _st.Unop("ROT");                  // [q, u, v]
        if (quotient)
        {
            _st.Drop();                   // [q, u]
            _st.Drop();                   // [q]
        }
        else
        {
            _st.Unop("ROT");              // [u, v, q]
            _st.Drop();                   // [u, v]
            _st.Drop();                   // [u]
        }
    }

    // ---------------------------------------------------------------------
    // Ternary and calls
    // ---------------------------------------------------------------------

    private void EmitTernary(CTernary t)
    {
        Mark(t.Line);
        string yes = NewLabel();
        string end = NewLabel();
        Emit(t.Cond);
        _st.JNZ(yes);
        Emit(t.Else);
        _lines.Add($"JMP {end}");
        EmitLabel(yes);
        Emit(t.Then);
        EmitLabel(end);
    }

    private void EmitCall(CCall c)
    {
        Mark(c.Line);
        if (c.Func is not CIdent fi)
            throw new CCException(new CCDiagnostic(c.Line, 1,
                "only direct function calls are supported (no function pointers)"));
        string name = fi.Name;

        if (name == "putchar")
        {
            // Built-in: store the character in output port 0xF000 (low byte)
            // and return it (left on the stack): c; DUP; PUSH16 0xF000; STORE.
            Emit(c.Args[0]);
            _st.Dup();
            _st.Push($"PUSH16 0xF000");
            _st.Store();
            return;
        }

        if (name == "getchar")
        {
            // Built-in: load input port 0xF001 (a byte, or 0xFFFF when exhausted).
            _st.Push("PUSH16 0xF001");
            _st.Load();
            return;
        }

        if (!_functions.TryGetValue(name, out var fs) || fs.Func is null)
            throw new CCException(new CCDiagnostic(c.Line, 1, $"'{name}' is not a function"));
        var fdef = fs.Func;
        // The callee's full frame (params + locals + spill budget — the
        // slot list includes the parameters): the prologue lowers SP by
        // exactly this, so old-SP − (frame − i) is the callee's slot i.
        int calleeFrame = _locals[name].Count + SpillBudget;

        // Arguments: the caller stores each into the callee's frame
        // (argument i at old-SP − (calleeFrame − i)), so the callee finds
        // them in slots 0..p−1.
        for (int i = 0; i < c.Args.Count; i++)
        {
            Emit(c.Args[i]);
            _st.Push($"PUSH16 0x{SpWord:X4}");
            _st.Load();
            _st.Push($"PUSH {calleeFrame - i}");
            _st.Op("SUB");
            _st.Store();
        }

        _lines.Add($"CALL {name}");
        if (fdef.ReturnType.Kind != CTypeKind.Void)
            _st.RecordValue();
    }

    // ---------------------------------------------------------------------
    // Type inference (coarse: enough for char sign rules and compare kinds)
    // ---------------------------------------------------------------------

    private CType InferType(CExpr e)
    {
        switch (e)
        {
            case CLit lit:
                if (lit.Kind == CLitKind.Char)
                    return CType.Char;
                // Natural type of an integer literal: values that only fit
                // in 0x8000..0xFFFF are unsigned (signed 16-bit tops out
                // at 0x7FFF) — this drives unsigned comparison/division.
                if (lit.Kind == CLitKind.Int && lit.Value > 0x7FFF)
                    return CType.Scalar(CBaseKind.Int, unsigned: true);
                return CType.Int;
            case CIdent id:
                if (FindLocal(id.Name) is { } local)
                    return local.Type;
                if (_globals.TryGetValue(id.Name, out var g) && g.Kind is CSymKind.Global)
                    return g.Type;
                return CType.Int;
            case CIndex ix:
                return InferElem(InferType(ix.Base));
            case CUnary u:
                return u.Op switch
                {
                    UOp.Deref => InferElem(InferType(u.Operand)),
                    UOp.AddrOf => CType.Pointer(InferType(u.Operand), false),
                    _ => CType.Int,
                };
            case CPostfix:
                return CType.Int;
            case CBinary b:
                if (b.Op is BOp.Assign or BOp.PlusAssign or BOp.MinusAssign or BOp.MulAssign or BOp.DivAssign
                    or BOp.ModAssign or BOp.AndAssign or BOp.OrAssign or BOp.XorAssign or BOp.ShlAssign or BOp.ShrAssign)
                    return InferType(b.Left);
                return CType.Int;
            case CTernary:
                return CType.Int;
            case CCall c:
                if (c.Func is CIdent ci && _functions.TryGetValue(ci.Name, out var f) && f.Func is not null)
                    return f.Func.ReturnType;
                return CType.Int;
            case CSizeof:
                return CType.Int;
            default:
                return CType.Int;
        }
    }

    private static CType InferElem(CType t)
        => t.Kind is CTypeKind.Pointer or CTypeKind.Array ? t.Element! : CType.Int;

    // ---------------------------------------------------------------------
    // Mnemonic mapping
    // ---------------------------------------------------------------------

    private static string BinaryMnemonic(BOp op) => op switch
    {
        BOp.Add => "ADD",
        BOp.Sub => "SUB",
        BOp.Mul => "MUL",
        BOp.Div => "DIV",
        BOp.Mod => "MOD",
        BOp.And => "AND",
        BOp.Or => "OR",
        BOp.Xor => "XOR",
        BOp.Eq => "EQ",
        BOp.Ne => "NE",
        BOp.Lt => "LT",
        BOp.Gt => "GT",
        BOp.Le => "LE",
        BOp.Ge => "GE",
        _ => throw new CCException(new CCDiagnostic(0, 0, $"no binary mnemonic for {op}")),
    };

    private static string CompareMnemonic(BOp op, bool unsigned) => op switch
    {
        BOp.Lt => unsigned ? "ULT" : "LT",
        BOp.Gt => unsigned ? "UGT" : "GT",
        BOp.Le => unsigned ? "ULE" : "LE",
        BOp.Ge => unsigned ? "UGE" : "GE",
        _ => BinaryMnemonic(op),
    };

    private static string CompoundMnemonic(BOp op) => op switch
    {
        BOp.PlusAssign => "ADD",
        BOp.MinusAssign => "SUB",
        BOp.MulAssign => "MUL",
        BOp.DivAssign => "DIV",
        BOp.ModAssign => "MOD",
        BOp.AndAssign => "AND",
        BOp.OrAssign => "OR",
        BOp.XorAssign => "XOR",
        BOp.ShlAssign => "SLL",
        BOp.ShrAssign => "SLR",
        _ => throw new CCException(new CCDiagnostic(0, 0, $"no compound-assign mnemonic for {op}")),
    };
}
