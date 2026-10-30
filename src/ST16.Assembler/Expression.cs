namespace ST16.Assembler;

/// <summary>
/// Result of parsing an operand expression (spec §8.2):
/// a constant, a label reference, or <c>label ± constant</c>.
/// </summary>
internal sealed class Expression
{
    /// <summary>True when the expression is a bare (signed) constant.</summary>
    public bool IsConstant { get; init; }

    /// <summary>Constant value (valid when <see cref="IsConstant"/>).</summary>
    public long Constant { get; init; }

    /// <summary>Referenced label (valid when <see cref="HasLabel"/>).</summary>
    public string? Label { get; init; }

    /// <summary>Constant added to the label address (valid when <see cref="HasLabel"/>).</summary>
    public long Offset { get; init; }

    public bool HasLabel => Label is not null;

    internal static bool TryParse(string text, out Expression expression, out string? error)
    {
        var s = text.Trim();
        if (s.Length == 0)
        {
            expression = null!;
            error = "missing operand";
            return false;
        }

        // Try a bare (signed) numeric literal first: [−]digits | [−]0x… | [−]0b…
        if (TryParseNumber(s, out long value))
        {
            expression = new Expression { IsConstant = true, Constant = value };
            error = null;
            return true;
        }

        // Otherwise: [label] [+|− constant]. Scan the label (leading identifier),
        // then an optional operator and constant.
        int i = 0;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
            i++;

        if (i == 0 || !IsValidLabel(s[..i]))
        {
            expression = null!;
            error = $"'{text}' is not a valid expression (expected a number, a label, or 'label ± constant')";
            return false;
        }

        var label = s[..i];
        var rest = s[i..].TrimStart();
        if (rest.Length == 0)
        {
            expression = new Expression { Label = label };
            error = null;
            return true;
        }

        if (rest.Length >= 2 && (rest[0] == '+' || rest[0] == '-' || rest[0] == '−'))
        {
            var numText = rest[1..].TrimStart();
            if (numText.Length > 0 && TryParseNumber(numText, out long constant))
            {
                var offset = rest[0] == '-' || rest[0] == '−' ? -constant : constant;
                expression = new Expression { Label = label, Offset = offset };
                error = null;
                return true;
            }
        }

        expression = null!;
        error = $"'{text}' is not a valid expression (v1 supports a constant, a label, or 'label ± constant')";
        return false;
    }

    /// <summary>Parse a literal: [−]decimal | [−]0x hex | [−]0b binary (spec §8.2).</summary>
    internal static bool TryParseNumber(string s, out long value)
    {
        value = 0;
        var t = s.Trim();
        if (t.Length == 0)
            return false;

        int start = 0;
        bool negative = false;
        if (t[0] == '-' || t[0] == '−')
        {
            negative = true;
            start = 1;
        }
        else if (t[0] == '+')
        {
            start = 1;
        }

        if (start >= t.Length)
            return false;

        ulong mag;
        bool isHex = t[start] == '0' && start + 1 < t.Length && (t[start + 1] == 'x' || t[start + 1] == 'X');
        bool isBin = t[start] == '0' && start + 1 < t.Length && (t[start + 1] == 'b' || t[start + 1] == 'B');

        if (isHex)
        {
            var hex = t[(start + 2)..];
            if (hex.Length == 0 || hex.Length > 8 || !System.Linq.Enumerable.All(hex, c => Uri.IsHexDigit(c)))
                return false;
            mag = Convert.ToUInt64(hex, 16);
        }
        else if (isBin)
        {
            var bin = t[(start + 2)..];
            if (bin.Length == 0 || bin.Length > 16 || !System.Linq.Enumerable.All(bin, c => c is '0' or '1'))
                return false;
            mag = Convert.ToUInt64(bin, 2);
        }
        else
        {
            if (!System.Linq.Enumerable.All(t[start..], c => char.IsDigit(c)))
                return false;
            if (!ulong.TryParse(t[start..], out mag))
                return false;
        }

        if (mag > (ulong)long.MaxValue)
            return false;

        value = negative ? -(long)mag : (long)mag;
        return true;
    }

    internal static bool IsValidLabel(string s)
    {
        if (s.Length == 0 || char.IsDigit(s[0]))
            return false;
        return s.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
