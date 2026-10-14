namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>ASCIIHexDecode</c> filter.</summary>
internal static class AsciiHexFilter
{
    /// <summary>
    /// Decodes <paramref name="data"/>, keeping at most <paramref name="maxLength"/> bytes;
    /// <paramref name="limited"/> says whether it decodes to more. The output is never larger than half the
    /// input, and is bounded all the same, like every filter's.
    /// </summary>
    /// <param name="data">The encoded data.</param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    /// <param name="cut">
    /// Whether the reader cut the data short at one of its limits: a digit left alone at its end, before any
    /// <c>&gt;</c>, is the first of a pair the cut parted, and is dropped rather than completed with a zero.
    /// </param>
    public static byte[] Decode(
        ReadOnlySpan<byte> data, out bool limited, int maxLength, bool cut = false)
    {
        limited = false;
        var ended = false;
        var buffer = new byte[Math.Min((data.Length / 2) + 1, Math.Max(maxLength, 0))];
        var length = 0;
        var high = -1;

        foreach (var current in data)
        {
            if (current == (byte)'>')
            {
                ended = true;
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

        if (high >= 0 && (ended || !cut))
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
