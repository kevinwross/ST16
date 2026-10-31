namespace ST16.CC;

/// <summary>
/// A single compiler diagnostic with a 1-based line and column in the
/// <c>.c</c> file (plan P4: "friendly, line-referenced parse and semantic
/// error messages — students read these").
/// </summary>
public sealed class CCDiagnostic
{
    public CCDiagnostic(int line, int column, string message)
    {
        Line = line;
        Column = column;
        Message = message;
    }

    public int Line { get; }
    public int Column { get; }
    public string Message { get; }

    public override string ToString() => $"{Line}:{Column}: error: {Message}";
}

/// <summary>
/// Compile-time failure (lexer, parser, or semantic analysis). Fail-fast:
/// the first diagnostic wins; the CLI prints it and exits 1.
/// </summary>
public sealed class CCException : Exception
{
    public CCException(CCDiagnostic diagnostic)
        : base(diagnostic.Message)
    {
        Diagnostic = diagnostic;
    }

    public CCDiagnostic Diagnostic { get; }
}
