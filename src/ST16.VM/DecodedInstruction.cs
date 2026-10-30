namespace ST16.VM;

/// <summary>A decoded instruction (fetch/decode result, spec §4).</summary>
public sealed record DecodedInstruction(
    /// <summary>PC of the instruction (the header word).</summary>
    ushort Pc,
    /// <summary>The defined opcode.</summary>
    Opcode Opcode,
    /// <summary>The header word as fetched.</summary>
    ushort RawWord,
    /// <summary>The low byte of the header word (operand field).</summary>
    ushort Operand,
    /// <summary>The second word for two-word instructions (PUSH16, JMP, CALL); null otherwise.</summary>
    ushort? SecondWord,
    /// <summary>Mnemonic from the frozen opcode table.</summary>
    string Mnemonic);
