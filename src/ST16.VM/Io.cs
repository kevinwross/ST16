using System.Text;

namespace ST16.VM;

/// <summary>
/// Console input (port 0xF001, spec §7). Reads are non-blocking: when no
/// input is available the machine reads 0xFFFF, it never stalls.
/// </summary>
public interface IInputSource
{
    /// <summary>Reads one input byte. Returns false (with value 0) when no input is available.</summary>
    bool TryReadByte(out byte value);
}

/// <summary>Console output (port 0xF000, spec §7): one byte = one character (low 8 bits).</summary>
public interface IOutputSink
{
    void WriteByte(byte value);
}

/// <summary>
/// A FIFO byte queue. Backs test inputs and the CLI's <c>in</c> command.
/// Once empty it stays empty: a finite input sequence that is exhausted reads
/// 0xFFFF forever (spec §7).
/// </summary>
public sealed class QueueInputSource : IInputSource
{
    private readonly Queue<byte> _queue = new();

    /// <summary>Bytes queued but not yet consumed.</summary>
    public int Count => _queue.Count;

    public void Enqueue(byte value) => _queue.Enqueue(value);

    public bool TryReadByte(out byte value)
    {
        if (_queue.Count == 0)
        {
            value = 0;
            return false;
        }
        value = _queue.Dequeue();
        return true;
    }
}

/// <summary>Collects output bytes as characters (tests, CLI console view).</summary>
public sealed class StringOutputSink : IOutputSink
{
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public void WriteByte(byte value) => _text.Append((char)value);

    public void Clear() => _text.Clear();
}
