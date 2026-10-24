namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>ASCIIHexDecode</c> filter.</summary>
internal static class AsciiHexFilter
{
    /// <summary>
    /// Decodes <paramref name="data"/>, keeping at most <paramref name="maxLength"/> bytes;
    /// <paramref name="limited"/> says whether it decodes to more. The output is never larger than half the
    /// input, and is bounded all the same, like every filter's.
    /// </summary>
    /// <remarks>
    /// What the format does not allow is decoded as other readers decode it, and counted in <paramref name="faults"/>:
    /// a byte that is neither a digit nor white space is passed over, and data without its end marker decodes to its
    /// end, a last digit without its pair completed with a 0 as one before the marker is.
    /// </remarks>
    /// <param name="data">The encoded data.</param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="faults">What the data holds that the format does not allow.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    /// <param name="cut">
    /// Whether the data ends where the file's may not — the reader cut it short at one of its limits, or a guard hides
    /// where it ends —: a digit left alone at its end, before any <c>&gt;</c>, is the first of a pair the cut parted, and is
    /// dropped rather than completed with a zero, though the byte counts against <paramref name="maxLength"/>, which
    /// <paramref name="limited"/> then says, and <see cref="DataFaults.Dropped"/> counts it.
    /// </param>
    public static byte[] Decode(
        ReadOnlySpan<byte> data, out bool limited, out DataFaults faults, int maxLength, bool cut = false)
    {
        limited = false;
        faults = default;
        var ended = false;
        var buffer = new byte[Math.Min((data.Length / 2) + 1, Math.Max(maxLength, 0))];
        var length = 0;
        var high = -1;

        for (var index = 0; index < data.Length; index++)
        {
            var current = data[index];

            if (current == (byte)'>')
            {
                ended = true;
                break;
            }

            var value = PdfCharacters.HexValue(current);
            if (value < 0)
            {
                // White space is allowed anywhere; anything else encodes nothing, and is passed over.
                if (!PdfCharacters.IsWhitespace(current))
                {
                    faults.Pass(index);
                }

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
                    return buffer.AsSpan(0, length).ToArray();
                }

                buffer[length++] = (byte)((high << 4) | value);
                high = -1;
            }
        }

        if (!ended)
        {
            faults.Ending = high < 0 ? DataEnding.Unmarked : DataEnding.UnmarkedInsideUnit;
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
        else if (high >= 0)
        {
            // Data that ends where the file's may not leaves its last byte half read, which the next digit decides: it is
            // not written, and the whole data, which holds it, counts it against the bound all the same.
            limited = length >= maxLength;
            faults.Dropped = 1;
        }

        return buffer.AsSpan(0, length).ToArray();
    }
}
