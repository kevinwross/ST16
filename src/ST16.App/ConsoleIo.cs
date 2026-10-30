using ST16.VM;

namespace ST16.App;

/// <summary>
/// Non-blocking console input over Console.In (port 0xF001, spec §7), built
/// on <see cref="TextReader.Peek"/>: the machine reads a byte only when one
/// is already queued. On an interactive terminal this blocks until a key is
/// pressed, so a program that polls 0xF001 in a tight loop waits for input.
/// With a redirected file or pipe the input is a finite sequence: once it is
/// exhausted, Peek reports EOF and every subsequent read yields 0xFFFF —
/// matching the spec's non-blocking semantics for finite input sequences.
/// </summary>
internal sealed class ConsoleInputSource : IInputSource
{
    private readonly TextReader _reader;

    public ConsoleInputSource(TextReader reader) => _reader = reader;

    public bool TryReadByte(out byte value)
    {
        try
        {
            if (_reader.Peek() == -1)
            {
                value = 0;
                return false;
            }
            value = (byte)_reader.Read();
            return true;
        }
        catch (IOException)
        {
            // A closed or failed stdin is treated as an exhausted input sequence (0xFFFF).
            value = 0;
            return false;
        }
    }
}

/// <summary>
/// Console output sink (port 0xF000, spec §7): one byte = one character on
/// stdout. Each byte is flushed so program output appears live — an
/// interactive prompt printed before a blocking read is visible before the
/// read stalls.
/// </summary>
internal sealed class ConsoleOutputSink : IOutputSink
{
    private readonly TextWriter _writer;

    public ConsoleOutputSink(TextWriter writer) => _writer = writer;

    public void WriteByte(byte value)
    {
        _writer.Write((char)value);
        _writer.Flush();
    }
}
