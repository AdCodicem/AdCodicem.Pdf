namespace AdCodicem.Pdf.IO;

/// <summary>Turns the raw bytes of a string token into the bytes the string actually denotes.</summary>
internal static class PdfStringDecoder
{
    /// <summary>Decodes the contents of a literal string, resolving backslash escapes.</summary>
    public static byte[] DecodeLiteral(ReadOnlySpan<byte> raw)
    {
        if (raw.IndexOf((byte)'\\') < 0 && raw.IndexOf((byte)'\r') < 0)
        {
            return raw.ToArray();
        }

        var buffer = new byte[raw.Length];
        var length = 0;
        var index = 0;

        while (index < raw.Length)
        {
            var current = raw[index];

            if (current == (byte)'\r')
            {
                // An end-of-line inside a string means a single line feed, however it was written.
                buffer[length++] = (byte)'\n';
                index++;
                if (index < raw.Length && raw[index] == (byte)'\n')
                {
                    index++;
                }

                continue;
            }

            if (current != (byte)'\\')
            {
                buffer[length++] = current;
                index++;
                continue;
            }

            index++;
            if (index >= raw.Length)
            {
                break;
            }

            var escaped = raw[index];
            switch (escaped)
            {
                case (byte)'n':
                    buffer[length++] = (byte)'\n';
                    index++;
                    break;
                case (byte)'r':
                    buffer[length++] = (byte)'\r';
                    index++;
                    break;
                case (byte)'t':
                    buffer[length++] = (byte)'\t';
                    index++;
                    break;
                case (byte)'b':
                    buffer[length++] = (byte)'\b';
                    index++;
                    break;
                case (byte)'f':
                    buffer[length++] = (byte)'\f';
                    index++;
                    break;
                case (byte)'\n':
                    // A backslash at end of line continues the string without adding anything.
                    index++;
                    break;
                case (byte)'\r':
                    index++;
                    if (index < raw.Length && raw[index] == (byte)'\n')
                    {
                        index++;
                    }

                    break;
                case >= (byte)'0' and <= (byte)'7':
                    var value = 0;
                    var digits = 0;
                    while (digits < 3 && index < raw.Length && raw[index] is >= (byte)'0' and <= (byte)'7')
                    {
                        value = (value * 8) + (raw[index] - '0');
                        index++;
                        digits++;
                    }

                    buffer[length++] = (byte)value;
                    break;
                default:
                    // Any other escaped byte stands for itself, parentheses and backslashes included.
                    buffer[length++] = escaped;
                    index++;
                    break;
            }
        }

        return buffer.AsSpan(0, length).ToArray();
    }

    /// <summary>
    /// Decodes the contents of a hexadecimal string, skipping white space, which ISO 32000-1 (7.3.4.3) lets it hold, and
    /// every other byte that is no hexadecimal digit, which it does not: those are counted, for the caller to report.
    /// </summary>
    /// <param name="raw">The bytes between the angle brackets.</param>
    /// <param name="firstStray">Where the first byte that is neither a hexadecimal digit nor white space lies, or -1.</param>
    /// <param name="strays">How many such bytes the string holds.</param>
    public static byte[] DecodeHex(ReadOnlySpan<byte> raw, out int firstStray, out int strays)
    {
        var buffer = new byte[(raw.Length / 2) + 1];
        var length = 0;
        var high = -1;
        firstStray = -1;
        strays = 0;

        for (var index = 0; index < raw.Length; index++)
        {
            var current = raw[index];
            var value = PdfCharacters.HexValue(current);
            if (value < 0)
            {
                if (!PdfCharacters.IsWhitespace(current) && strays++ == 0)
                {
                    firstStray = index;
                }

                continue;
            }

            if (high < 0)
            {
                high = value;
            }
            else
            {
                buffer[length++] = (byte)((high << 4) | value);
                high = -1;
            }
        }

        if (high >= 0)
        {
            // An odd number of digits: the specification says the missing one is zero.
            buffer[length++] = (byte)(high << 4);
        }

        return buffer.AsSpan(0, length).ToArray();
    }

    /// <summary>
    /// Decodes a name, resolving <c>#xx</c> escapes. A number sign that two hexadecimal digits do not follow is kept as
    /// the byte it is, as PDF 1.1 read it and most readers still do; ISO 32000-1 (7.3.5) has a writer write it <c>#23</c>.
    /// </summary>
    /// <param name="raw">The bytes after the solidus.</param>
    /// <param name="badEscape">Where the first number sign kept so lies, or -1.</param>
    public static string DecodeName(ReadOnlySpan<byte> raw, out int badEscape)
    {
        badEscape = -1;

        if (raw.IsEmpty)
        {
            return string.Empty;
        }

        if (raw.IndexOf((byte)'#') < 0)
        {
            return System.Text.Encoding.Latin1.GetString(raw);
        }

        Span<byte> buffer = raw.Length <= 128 ? stackalloc byte[raw.Length] : new byte[raw.Length];
        var length = 0;
        var index = 0;

        while (index < raw.Length)
        {
            if (raw[index] == (byte)'#')
            {
                if (index + 2 < raw.Length)
                {
                    var high = PdfCharacters.HexValue(raw[index + 1]);
                    var low = PdfCharacters.HexValue(raw[index + 2]);

                    if (high >= 0 && low >= 0)
                    {
                        buffer[length++] = (byte)((high << 4) | low);
                        index += 3;
                        continue;
                    }
                }

                if (badEscape < 0)
                {
                    badEscape = index;
                }
            }

            buffer[length++] = raw[index];
            index++;
        }

        return System.Text.Encoding.Latin1.GetString(buffer[..length]);
    }
}
