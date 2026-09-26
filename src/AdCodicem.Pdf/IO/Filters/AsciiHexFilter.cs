namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>ASCIIHexDecode</c> filter.</summary>
internal static class AsciiHexFilter
{
    /// <summary>
    /// Decodes <paramref name="data"/>, keeping at most <paramref name="maxLength"/> bytes;
    /// <paramref name="limited"/> says whether it decodes to more. The output is never larger than half the
    /// input, and is bounded all the same, like every filter's.
    /// </summary>
    public static byte[] Decode(
        ReadOnlySpan<byte> data, out bool limited, int maxLength)
    {
        limited = false;
        var buffer = new byte[Math.Min((data.Length / 2) + 1, Math.Max(maxLength, 0))];
        var length = 0;
        var high = -1;

        foreach (var current in data)
        {
            if (current == (byte)'>')
            {
                break;
            }

            var value = PdfCharacters.HexValue(current);
            if (value < 0)
            {
                // White space is allowed anywhere; anything else is skipped rather than fatal.
                continue;
            }

            if (high < 0)
            {
                high = value;
            }
            else
            {
                if (length == buffer.Length)
                {
                    limited = true;
                    high = -1;
                    break;
                }

                buffer[length++] = (byte)((high << 4) | value);
                high = -1;
            }
        }

        if (high >= 0)
        {
            if (length < buffer.Length)
            {
                buffer[length++] = (byte)(high << 4);
            }
            else
            {
                limited = true;
            }
        }

        return buffer.AsSpan(0, length).ToArray();
    }
}
