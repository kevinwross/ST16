namespace ST16.CC;

/// <summary>
/// Tracks the code generator's logical operand stack and spills the oldest
/// live value into a frame spill slot when the machine's 32-slot operand
/// stack would be exceeded (plan P4: "spilling at depth 28, visible in -S").
///
/// Each machine value the generator pushes is an <see cref="Entry"/>. When a
/// value is spilled it keeps its entry (marked not-live) with its frame slot.
/// Invariant after every operation: all not-live entries precede all live
/// entries, so the top two entries are always live and the oldest live
/// entry is always the physical bottom of the machine stack.
///
/// Spill: the oldest live value is the physical bottom; one ROT brings it to
/// the top, then the STORE idiom consumes it. Reload: a spilled entry is only
/// ever the logical top when every newer value has been consumed, so pushing
/// it back with LOAD restores the correct order.
/// </summary>
public sealed class CStackManager
{
    private sealed class Entry
    {
        public Entry(bool live, int slot)
        {
            Live = live;
            Slot = slot;
        }

        public bool Live;
        public int Slot;
    }

    private readonly Action<string> _emit;
    private readonly int _spillSlotBase;
    private readonly int _maxDepth;
    private readonly int _spillBudget;
    private readonly List<Entry> _entries = new();
    private int _spillCount;

    /// <param name="emit">Receives each assembly line to emit.</param>
    /// <param name="spillSlotBase">First spill slot in the current frame (after the params/locals).</param>
    /// <param name="maxDepth">Maximum live values (28, under the VM's 32).</param>
    /// <param name="spillBudget">Maximum concurrent spills (16).</param>
    public CStackManager(Action<string> emit, int spillSlotBase, int maxDepth, int spillBudget)
    {
        _emit = emit;
        _spillSlotBase = spillSlotBase;
        _maxDepth = maxDepth;
        _spillBudget = spillBudget;
    }

    /// <summary>The machine instruction leaves a fresh value; record it.</summary>
    public void Push(string pushInstruction)
    {
        _emit(pushInstruction);
        RecordValue();
    }

    /// <summary>
    /// Records a value produced by the machine itself (e.g. a function
    /// result on RET) — no instruction line is emitted.
    /// </summary>
    public void RecordValue()
    {
        _entries.Add(new Entry(live: true, slot: -1));
        EnsureLimit();
    }

    /// <summary>LOAD: the address entry becomes the value entry (net zero).</summary>
    public void Load()
    {
        ReloadTopIfHole();
        _emit("LOAD");
    }

    /// <summary>STORE: the top two (value, address) are consumed.</summary>
    public void Store()
    {
        EnsureTopLive(2);
        _emit("STORE");
        _entries.RemoveAt(_entries.Count - 1);
        _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>DUP the top (reloaded first if it is a hole).</summary>
    public void Dup()
    {
        ReloadTopIfHole();
        _emit("DUP");
        _entries.Add(new Entry(live: true, slot: -1));
        EnsureLimit();
    }

    /// <summary>SWAP the top two (both made live first — a spilled second is reloaded).</summary>
    public void Swap()
    {
        EnsureTopLive(2);
        _emit("SWAP");
        var t = _entries[^1];
        _entries[^1] = _entries[^2];
        _entries[^2] = t;
    }

    /// <summary>Discard the top value (DROP when live; a hole is just forgotten).</summary>
    public void Drop()
    {
        var top = _entries[^1];
        if (top.Live)
            _emit("DROP");
        _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>
    /// A binary instruction: the top two are consumed, one result remains
    /// (the top entry is dropped; the second becomes the result). A spilled
    /// second value is reloaded first (and SWAPped back under the live top).
    /// </summary>
    public void Op(string binaryInstruction)
    {
        EnsureTopLive(2);
        _emit(binaryInstruction);
        _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>A unary instruction (NEG, NOT, sign-extend halves…): net zero.</summary>
    public void Unop(string unaryInstruction)
    {
        ReloadTopIfHole();
        _emit(unaryInstruction);
    }

    /// <summary>JZ/JNZ: the VM pops the condition unconditionally.</summary>
    public void JZ(string label)
    {
        ReloadTopIfHole();
        _emit($"JZ {label}");
        _entries.RemoveAt(_entries.Count - 1);
    }

    public void JNZ(string label)
    {
        ReloadTopIfHole();
        _emit($"JNZ {label}");
        _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>
    /// The top value is about to be consumed by an instruction the caller
    /// emits (e.g. a STORE-into-slot idiom): reload it first if it is a hole.
    /// </summary>
    public void ConsumeTop()
    {
        ReloadTopIfHole();
        _entries.RemoveAt(_entries.Count - 1);
    }

    /// <summary>
    /// Makes the top <paramref name="n"/> entries physically present.
    /// By the invariant the live entries are a suffix: the top is a hole
    /// only when nothing is live, and the second is a hole only when
    /// exactly one value is live. A reloaded second value lands on top
    /// (the LOAD idiom pushes), so it is SWAPped back under the live value
    /// to keep the logical order (a − b must see a below b).
    /// </summary>
    private void EnsureTopLive(int n)
    {
        if (n <= 1)
        {
            ReloadTopIfHole();
            return;
        }

        if (_entries.Count < 2)
        {
            // One value tracked: the machine will fault with STACKUF if the
            // instruction runs; the manager must not crash first.
            ReloadTopIfHole();
            return;
        }

        if (!_entries[^1].Live)
        {
            // Nothing is live; reload the top and let the machine decide
            // (codegen never consumes two values when zero are live).
            ReloadTopIfHole();
            return;
        }

        if (!_entries[^2].Live)
        {
            ReloadEntry(_entries[^2]); // the LOAD idiom pushes onto the top
            _emit("SWAP");             // back under the live value
            var t = _entries[^1];
            _entries[^1] = _entries[^2];
            _entries[^2] = t;
        }
    }

    /// <summary>Number of live (physically present) entries.</summary>
    public int LiveCount => _entries.Count(e => e.Live);

    /// <summary>Clears all state (between functions).</summary>
    public void Reset()
    {
        _entries.Clear();
        _spillCount = 0;
    }

    // ---------------------------------------------------------------------

    private void EnsureLimit()
    {
        while (LiveCount > _maxDepth)
            SpillOldest();
    }

    private void SpillOldest()
    {
        int live = LiveCount;
        if (live < 2)
            throw new CCException(new CCDiagnostic(0, 0,
                "internal compiler error: cannot spill the only live value"));
        if (_spillCount >= _spillBudget)
            throw new CCException(new CCDiagnostic(0, 0,
                "expression too complex: too many live values for the ST16 operand stack (32 slots) with the 16-slot spill budget"));

        int first = 0;
        while (!_entries[first].Live)
            first++;

        _emit("ROT");
        var bottom = _entries[first];
        for (int i = first; i < _entries.Count - 1; i++)
            _entries[i] = _entries[i + 1];
        _entries[^1] = bottom;

        int slot = _spillSlotBase + _spillCount++;
        bottom.Live = false;
        bottom.Slot = slot;

        _emit("PUSH16 0xE000");
        _emit("LOAD");
        _emit($"PUSH {slot}");
        _emit("ADD");
        _emit("STORE");

        Compact();
    }

    /// <summary>Move all holes (not-live entries) before all live entries.</summary>
    private void Compact()
    {
        var holes = new List<Entry>();
        var live = new List<Entry>();
        foreach (var e in _entries)
            (e.Live ? live : holes).Add(e);
        _entries.Clear();
        _entries.AddRange(holes);
        _entries.AddRange(live);
    }

    private void ReloadTopIfHole()
    {
        var top = _entries[^1];
        if (top.Live)
            return;
        ReloadEntry(top);
    }

    /// <summary>Emits the reload idiom for a spilled entry and marks it live.</summary>
    private void ReloadEntry(Entry e)
    {
        _emit("PUSH16 0xE000");
        _emit("LOAD");
        _emit($"PUSH {e.Slot}");
        _emit("ADD");
        _emit("LOAD");
        e.Live = true;
    }
}
