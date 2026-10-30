namespace ST16.VM;

/// <summary>
/// 16-bit two's-complement value (spec §2.1). All arithmetic wraps modulo 2¹⁶;
/// ST16 has no overflow fault.
/// </summary>
public readonly struct Word : IEquatable<Word>
{
    /// <summary>The raw 16-bit pattern (0x0000–0xFFFF).</summary>
    public ushort Raw { get; }

    public Word(ushort raw) => Raw = raw;

    /// <summary>Wraps an integer into its low 16 bits.</summary>
    public static Word From(int value) => new((ushort)(value & 0xFFFF));

    /// <summary>Two's-complement (signed) interpretation: −32768…32767.</summary>
    public int Signed => (short)Raw;

    public static Word operator +(Word a, Word b) => new((ushort)(a.Raw + b.Raw));
    public static Word operator -(Word a, Word b) => new((ushort)(a.Raw - b.Raw));
    public static Word operator *(Word a, Word b) => new((ushort)(a.Raw * b.Raw));
    public static Word operator &(Word a, Word b) => new((ushort)(a.Raw & b.Raw));
    public static Word operator |(Word a, Word b) => new((ushort)(a.Raw | b.Raw));
    public static Word operator ^(Word a, Word b) => new((ushort)(a.Raw ^ b.Raw));
    public static Word operator ~(Word a) => new((ushort)~a.Raw);

    /// <summary>Negation with wrap: NEG 0x8000 yields 0x8000 (spec §5.2).</summary>
    public static Word Negate(Word a) => new((ushort)(0 - a.Raw));

    /// <summary>
    /// Signed division, quotient truncated toward zero (spec §5.2 + erratum).
    /// <paramref name="b"/> must be nonzero — the machine faults with DIV0 before
    /// calling. 0x8000 ÷ −1 wraps to 0x8000; no fault.
    /// </summary>
    public static Word TruncDivide(Word a, Word b) => From(a.Signed / b.Signed);

    /// <summary>
    /// Remainder with the sign of the dividend (C-99 style, spec §5.2).
    /// <paramref name="b"/> must be nonzero — the machine faults with DIV0 before
    /// calling. <c>x mod x = 0</c> for any x ≠ 0.
    /// </summary>
    public static Word Mod(Word a, Word b) => From(a.Signed % b.Signed);

    /// <summary>Shift left; count masked to 0–15 (spec §5.2).</summary>
    public static Word ShiftLeft(Word value, int count) => new((ushort)(value.Raw << (count & 0xF)));

    /// <summary>Arithmetic (sign-extending) shift right; count masked to 0–15 (spec §5.2).</summary>
    public static Word ShiftRightArithmetic(Word value, int count)
        => new((ushort)((short)value.Raw >> (count & 0xF)));

    // Signed comparisons (spec §5.3).
    public static bool Slt(Word a, Word b) => a.Signed < b.Signed;
    public static bool Sgt(Word a, Word b) => a.Signed > b.Signed;
    public static bool Sle(Word a, Word b) => a.Signed <= b.Signed;
    public static bool Sge(Word a, Word b) => a.Signed >= b.Signed;

    // Unsigned comparisons (spec §5.3).
    public static bool Ult(Word a, Word b) => a.Raw < b.Raw;
    public static bool Ugt(Word a, Word b) => a.Raw > b.Raw;
    public static bool Ule(Word a, Word b) => a.Raw <= b.Raw;
    public static bool Uge(Word a, Word b) => a.Raw >= b.Raw;

    /// <summary>Sign-extends an 8-bit signed immediate to 16 bits (PUSH, spec §4 + erratum).</summary>
    public static Word SignExtend8(byte value) => new((ushort)(ushort)(sbyte)value);

    public bool Equals(Word other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Word other && Equals(other);
    public override int GetHashCode() => Raw.GetHashCode();
    public static bool operator ==(Word a, Word b) => a.Raw == b.Raw;
    public static bool operator !=(Word a, Word b) => a.Raw != b.Raw;
    public override string ToString() => $"0x{Raw:X4}";
}
