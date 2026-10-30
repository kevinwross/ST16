namespace ST16.VM;

/// <summary>The 37 defined ST16 opcodes (spec §5). Opcodes 0x25–0x3F are reserved (ILLOP).</summary>
public enum Opcode : byte
{
    // Stack manipulation (0x00–0x04).
    Dup = 0x00,
    Swap = 0x01,
    Over = 0x02,
    Rot = 0x03,
    Drop = 0x04,

    // Arithmetic (0x05–0x10).
    Add = 0x05,
    Sub = 0x06,
    Neg = 0x07,
    Mul = 0x08,
    Div = 0x09,
    Mod = 0x0A,
    And = 0x0B,
    Or = 0x0C,
    Xor = 0x0D,
    Not = 0x0E,
    Sll = 0x0F,
    Slr = 0x10,

    // Comparison (0x11–0x1A).
    Eq = 0x11,
    Ne = 0x12,
    Lt = 0x13,
    Gt = 0x14,
    Le = 0x15,
    Ge = 0x16,
    Ult = 0x17,
    Ugt = 0x18,
    Ule = 0x19,
    Uge = 0x1A,

    // Memory (0x1B–0x1E).
    Push = 0x1B,
    Push16 = 0x1C,
    Load = 0x1D,
    Store = 0x1E,

    // Control flow (0x1F–0x24).
    Jmp = 0x1F,
    Call = 0x20,
    Ret = 0x21,
    Jz = 0x22,
    Jnz = 0x23,
    Halt = 0x24,
}

/// <summary>Meaning of the low byte of the header word for a given opcode (spec §4).</summary>
public enum OperandKind
{
    /// <summary>Reserved; a conforming implementation must not depend on it (assemblers emit 0).</summary>
    Ignored,

    /// <summary>Two's-complement 8-bit immediate, −128…127 (PUSH).</summary>
    Signed8Immediate,

    /// <summary>Two's-complement 8-bit offset from PC+1, −127…+127 (JZ, JNZ).</summary>
    Signed8Relative,

    /// <summary>16-bit absolute value in the following word (PUSH16, JMP, CALL).</summary>
    Absolute16,
}

/// <summary>Frozen per-opcode facts (spec §4–§5).</summary>
public sealed record OpcodeInfo(string Mnemonic, bool TwoWord, OperandKind Operand, int MinStackDepth);

/// <summary>The frozen v1.0 opcode table (spec §5): 37 defined opcodes; anything else is ILLOP.</summary>
public static class OpcodeTable
{
    /// <summary>Highest defined opcode byte (0x24 = HALT).</summary>
    public const byte LastDefinedOpcode = (byte)Opcode.Halt;

    private static readonly Dictionary<Opcode, OpcodeInfo> Table = new()
    {
        [Opcode.Dup] = new("DUP", false, OperandKind.Ignored, 1),
        [Opcode.Swap] = new("SWAP", false, OperandKind.Ignored, 2),
        [Opcode.Over] = new("OVER", false, OperandKind.Ignored, 2),
        [Opcode.Rot] = new("ROT", false, OperandKind.Ignored, 3),
        [Opcode.Drop] = new("DROP", false, OperandKind.Ignored, 1),
        [Opcode.Add] = new("ADD", false, OperandKind.Ignored, 2),
        [Opcode.Sub] = new("SUB", false, OperandKind.Ignored, 2),
        [Opcode.Neg] = new("NEG", false, OperandKind.Ignored, 1),
        [Opcode.Mul] = new("MUL", false, OperandKind.Ignored, 2),
        [Opcode.Div] = new("DIV", false, OperandKind.Ignored, 2),
        [Opcode.Mod] = new("MOD", false, OperandKind.Ignored, 2),
        [Opcode.And] = new("AND", false, OperandKind.Ignored, 2),
        [Opcode.Or] = new("OR", false, OperandKind.Ignored, 2),
        [Opcode.Xor] = new("XOR", false, OperandKind.Ignored, 2),
        [Opcode.Not] = new("NOT", false, OperandKind.Ignored, 1),
        [Opcode.Sll] = new("SLL", false, OperandKind.Ignored, 2),
        [Opcode.Slr] = new("SLR", false, OperandKind.Ignored, 2),
        [Opcode.Eq] = new("EQ", false, OperandKind.Ignored, 2),
        [Opcode.Ne] = new("NE", false, OperandKind.Ignored, 2),
        [Opcode.Lt] = new("LT", false, OperandKind.Ignored, 2),
        [Opcode.Gt] = new("GT", false, OperandKind.Ignored, 2),
        [Opcode.Le] = new("LE", false, OperandKind.Ignored, 2),
        [Opcode.Ge] = new("GE", false, OperandKind.Ignored, 2),
        [Opcode.Ult] = new("ULT", false, OperandKind.Ignored, 2),
        [Opcode.Ugt] = new("UGT", false, OperandKind.Ignored, 2),
        [Opcode.Ule] = new("ULE", false, OperandKind.Ignored, 2),
        [Opcode.Uge] = new("UGE", false, OperandKind.Ignored, 2),
        [Opcode.Push] = new("PUSH", false, OperandKind.Signed8Immediate, 0),
        [Opcode.Push16] = new("PUSH16", true, OperandKind.Absolute16, 0),
        [Opcode.Load] = new("LOAD", false, OperandKind.Ignored, 1),
        [Opcode.Store] = new("STORE", false, OperandKind.Ignored, 2),
        [Opcode.Jmp] = new("JMP", true, OperandKind.Absolute16, 0),
        [Opcode.Call] = new("CALL", true, OperandKind.Absolute16, 0),
        [Opcode.Ret] = new("RET", false, OperandKind.Ignored, 0),
        [Opcode.Jz] = new("JZ", false, OperandKind.Signed8Relative, 1),
        [Opcode.Jnz] = new("JNZ", false, OperandKind.Signed8Relative, 1),
        [Opcode.Halt] = new("HALT", false, OperandKind.Ignored, 0),
    };

    /// <summary>True when the 8-bit opcode byte (bits 15–8 of the header word) is a defined v1.0 opcode.</summary>
    public static bool IsDefined(byte opcodeByte)
        => (opcodeByte & 0xC0) == 0 && opcodeByte <= LastDefinedOpcode;

    public static OpcodeInfo Get(Opcode op) => Table[op];

    /// <summary>37 — the number of defined opcodes in the frozen v1.0 table.</summary>
    public static int DefinedCount => Table.Count;
}
