namespace ST16.VM;

/// <summary>
/// ST16 memory (spec §3): 61,440 RAM words at 0x0000–0xEFFF plus the
/// 4,096-word I/O port region 0xF000–0xFFFF. Port behavior (spec §7) lives in
/// <see cref="ST16Machine"/>; this class is the RAM half.
/// </summary>
public sealed class Memory
{
    /// <summary>RAM size: 61,440 words (120 KiB).</summary>
    public const ushort RamSize = 0xF000;

    /// <summary>First port address.</summary>
    public const ushort PortStart = 0xF000;

    /// <summary>Console out: STORE outputs the low 8 bits; LOAD reads 0 (spec §7).</summary>
    public const ushort ConsoleOutPort = 0xF000;

    /// <summary>Console in: LOAD reads the next input byte or 0xFFFF (spec §7); STORE is a no-op.</summary>
    public const ushort ConsoleInPort = 0xF001;

    /// <summary>Instruction counter (low 16 bits): LOAD reads it; STORE is a no-op (spec §7).</summary>
    public const ushort InstructionCounterPort = 0xF002;

    /// <summary>Value read from console in when no input is available.</summary>
    public const ushort ConsoleInNoInput = 0xFFFF;

    private readonly ushort[] _ram = new ushort[RamSize];

    public static bool IsRamAddress(ushort address) => address < PortStart;

    public ushort Read(ushort address) => _ram[address];

    public void Write(ushort address, ushort value) => _ram[address] = value;

    public void Clear() => Array.Clear(_ram, 0, _ram.Length);

    public ushort[] FullCopy() => (ushort[])_ram.Clone();

    public void Restore(ushort[] ram)
    {
        if (ram.Length != RamSize)
            throw new ArgumentException($"RAM image must be exactly {RamSize} words.", nameof(ram));
        Array.Copy(ram, _ram, RamSize);
    }

    /// <summary>Copies <paramref name="count"/> RAM words starting at <paramref name="start"/>.</summary>
    public ushort[] Window(ushort start, int count)
    {
        if (start >= PortStart || count < 0 || start + count > RamSize)
            throw new ArgumentOutOfRangeException(nameof(start), "Window must lie inside the RAM region.");
        var copy = new ushort[count];
        Array.Copy(_ram, start, copy, 0, count);
        return copy;
    }
}
