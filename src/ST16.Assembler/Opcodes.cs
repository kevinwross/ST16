namespace ST16.Assembler;

/// <summary>
/// ST16 v1 opcode table (spec §5) and word-encoding helpers.
///
/// Encoding (per spec §9.3 golden words): <c>word = (opcode &lt;&lt; 8) | operandByte</c>.
/// The 6-bit opcode occupies bits 13..8 of the word (the top two bits of any
/// instruction header are 0) and the operand field is the low byte (bits 7..0).
/// Two-word instructions (PUSH16/JMP/CALL) carry operand field 0 in their
/// header word and are followed by a raw absolute 16-bit value word.
/// </summary>
public static class Opcodes
{
    /// <summary>Last RAM word address (61,440 words at 0x0000–0xEFFF, spec §3).</summary>
    public const int RamEnd = 0xEFFF;

    /// <summary>What the operand field means for a given instruction (spec §4).</summary>
    public enum OperandKind
    {
        /// <summary>Operand field ignored; the assembler emits 0 (spec §4: "reserved").</summary>
        None,

        /// <summary>PUSH: signed 8-bit immediate; range −128…127 is checked at assembly time.</summary>
        SignedImmediate,

        /// <summary>JZ/JNZ: signed 8-bit offset from the next instruction; range −128…127 is checked.</summary>
        RelativeOffset,

        /// <summary>PUSH16/JMP/CALL: absolute 16-bit value in the following word (0…65535).</summary>
        Absolute16,
    }

    /// <summary>One defined instruction: mnemonic, 6-bit opcode, operand-field meaning.</summary>
    public sealed record InstructionInfo(string Mnemonic, byte Opcode, OperandKind Operand)
    {
        /// <summary>True for PUSH16/JMP/CALL (two-word form, spec §4).</summary>
        public bool IsTwoWord => Operand == OperandKind.Absolute16;
    }

    private static readonly (string Mnemonic, byte Op, OperandKind Kind)[] Entries =
    {
        // §5.1 Stack manipulation (0x00–0x04)
        ("DUP", 0x00, OperandKind.None),
        ("SWAP", 0x01, OperandKind.None),
        ("OVER", 0x02, OperandKind.None),
        ("ROT", 0x03, OperandKind.None),
        ("DROP", 0x04, OperandKind.None),
        // §5.2 Arithmetic (0x05–0x10)
        ("ADD", 0x05, OperandKind.None),
        ("SUB", 0x06, OperandKind.None),
        ("NEG", 0x07, OperandKind.None),
        ("MUL", 0x08, OperandKind.None),
        ("DIV", 0x09, OperandKind.None),
        ("MOD", 0x0A, OperandKind.None),
        ("AND", 0x0B, OperandKind.None),
        ("OR", 0x0C, OperandKind.None),
        ("XOR", 0x0D, OperandKind.None),
        ("NOT", 0x0E, OperandKind.None),
        ("SLL", 0x0F, OperandKind.None),
        ("SLR", 0x10, OperandKind.None),
        // §5.3 Comparison (0x11–0x1A)
        ("EQ", 0x11, OperandKind.None),
        ("NE", 0x12, OperandKind.None),
        ("LT", 0x13, OperandKind.None),
        ("GT", 0x14, OperandKind.None),
        ("LE", 0x15, OperandKind.None),
        ("GE", 0x16, OperandKind.None),
        ("ULT", 0x17, OperandKind.None),
        ("UGT", 0x18, OperandKind.None),
        ("ULE", 0x19, OperandKind.None),
        ("UGE", 0x1A, OperandKind.None),
        // §5.4 Memory (0x1B–0x1E)
        ("PUSH", 0x1B, OperandKind.SignedImmediate),
        ("PUSH16", 0x1C, OperandKind.Absolute16),
        ("LOAD", 0x1D, OperandKind.None),
        ("STORE", 0x1E, OperandKind.None),
        // §5.5 Control flow (0x1F–0x24)
        ("JMP", 0x1F, OperandKind.Absolute16),
        ("CALL", 0x20, OperandKind.Absolute16),
        ("RET", 0x21, OperandKind.None),
        ("JZ", 0x22, OperandKind.RelativeOffset),
        ("JNZ", 0x23, OperandKind.RelativeOffset),
        ("HALT", 0x24, OperandKind.None),
    };

    private static readonly Dictionary<string, InstructionInfo> TableImpl =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly InstructionInfo[] ByOpTable = new InstructionInfo[64];

    /// <summary>Defined instructions keyed by mnemonic (case-insensitive; spec §8.1).</summary>
    public static IReadOnlyDictionary<string, InstructionInfo> Table { get; } = TableImpl;

    static Opcodes()
    {
        foreach (var (mnemonic, op, kind) in Entries)
        {
            var info = new InstructionInfo(mnemonic, op, kind);
            TableImpl.Add(mnemonic, info);
            ByOpTable[op] = info;
        }
    }

    /// <summary>Looks up a mnemonic case-insensitively; false for unknown mnemonics.</summary>
    public static bool TryGet(string mnemonic, out InstructionInfo info) =>
        Table.TryGetValue(mnemonic, out info);

    /// <summary>Defined instruction for a 6-bit opcode, or null for reserved 0x25–0x3F (ILLOP).</summary>
    public static InstructionInfo ByOpcode(byte op) => op < ByOpTable.Length ? ByOpTable[op] : null;

    /// <summary>Builds a header word: opcode in bits 13..8, operand field in the low byte.</summary>
    public static ushort MakeWord(byte opcode, int operandField) =>
        (ushort)((opcode << 8) | (operandField & 0xFF));

    /// <summary>Extracts the 6-bit opcode from a word (bits 13..8).</summary>
    public static byte OpcodeOf(ushort word) => (byte)((word >> 8) & 0x3F);

    /// <summary>Extracts the operand field as a signed byte (bits 7..0, sign-extended).</summary>
    public static int SignedOperand(ushort word) => (sbyte)(word & 0xFF);
}
