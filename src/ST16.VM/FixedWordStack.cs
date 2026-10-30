namespace ST16.VM;

/// <summary>
/// Fixed-depth 16-bit stack with a top-first API (slot s1 = top, spec §2.1).
/// Physically the slots grow from the bottom of the array (index 0 = bottom),
/// so every public accessor translates "slot from the top" to a physical index.
/// The machine checks underflow/overflow before mutating, so the operations
/// here assume the depth preconditions and never throw on depth.
/// </summary>
public sealed class FixedWordStack
{
    private readonly ushort[] _slots;
    private int _count;

    public FixedWordStack(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Stack capacity must be at least 1.");
        _slots = new ushort[capacity];
    }

    public int Capacity => _slots.Length;
    public int Count => _count;
    public bool IsEmpty => _count == 0;
    public bool IsFull => _count == _slots.Length;

    /// <summary>Top slot (s1). The caller guarantees Count ≥ 1.</summary>
    public ushort Top => _slots[_count - 1];

    /// <summary>Slot <paramref name="slot"/> from the top (0-based). The caller guarantees slot &lt; Count.</summary>
    public ushort At(int slot) => _slots[_count - 1 - slot];

    /// <summary>Writes slot <paramref name="slot"/> from the top (0-based). The caller guarantees slot &lt; Count.</summary>
    public void SetSlot(int slot, ushort value) => _slots[_count - 1 - slot] = value;

    public void Push(ushort value) => _slots[_count++] = value;

    public ushort Pop() => _slots[--_count];

    public void SwapTop2()
    {
        ushort top = _slots[_count - 1];
        _slots[_count - 1] = _slots[_count - 2];
        _slots[_count - 2] = top;
    }

    public void Clear() => _count = 0;

    /// <summary>Copies the occupied slots, top first.</summary>
    public ushort[] TopDownCopy()
    {
        var copy = new ushort[_count];
        for (int i = 0; i < _count; i++)
            copy[i] = _slots[_count - 1 - i];
        return copy;
    }

    /// <summary>Restores the occupied slots from a top-down copy (snapshot restore).</summary>
    public void Restore(ushort[] topDown, int count)
    {
        if (count > _slots.Length)
            throw new ArgumentException(
                "Snapshot stack depth exceeds this machine's configured depth.", nameof(topDown));
        if (topDown.Length < count)
            throw new ArgumentException(
                "Snapshot stack copy is shorter than its recorded count.", nameof(topDown));
        for (int i = 0; i < count; i++)
            _slots[count - 1 - i] = topDown[i];
        _count = count;
    }
}
