using System.Globalization;

namespace ST16.App;

/// <summary>
/// Number parsing for the CLI. Accepts non-negative decimal, 0x/0X hex, and
/// 0b/0B binary literals; anything else (including signs) is malformed.
/// </summary>
internal static class Numbers
{
    /// <summary>Parses a non-negative number in decimal, hex (0x/0X), or binary (0b/0B).</summary>
    public static bool TryParse(string text, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string digits = text;
        int radix = 10;
        if (TryStripPrefix(digits, 'x', 'X', out string hexDigits))
        {
            digits = hexDigits;
            radix = 16;
        }
        else if (TryStripPrefix(digits, 'b', 'B', out string binDigits))
        {
            digits = binDigits;
            radix = 2;
        }

        if (digits.Length == 0)
            return false;

        try
        {
            return radix switch
            {
                16 => long.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value),
                2 => TryParseBinary(digits, out value),
                _ => long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value),
            };
        }
        catch (OverflowException)
        {
            return false; // valid digits, beyond long range → out of range
        }
    }

    /// <summary>Parses a value that must fit in 16 bits (addresses).</summary>
    public static bool TryParseAddress(string text, out ushort address)
    {
        if (!TryParse(text, out long v) || v < 0 || v > 0xFFFF)
        {
            address = 0;
            return false;
        }
        address = (ushort)v;
        return true;
    }

    private static bool TryStripPrefix(string text, char lower, char upper, out string rest)
    {
        if (text.Length >= 2 && text[0] == '0' && (text[1] == lower || text[1] == upper))
        {
            rest = text[2..];
            return true;
        }
        rest = text;
        return false;
    }

    private static bool TryParseBinary(string digits, out long value)
    {
        long result = 0;
        foreach (char c in digits)
        {
            if (c is not ('0' or '1'))
            {
                value = 0;
                return false;
            }
            result = (result << 1) | (long)(c - '0');
            if (result < 0) // magnitude overflowed the 63-bit signed range
            {
                value = 0;
                return false;
            }
        }
        value = result;
        return true;
    }
}
