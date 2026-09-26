namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>LZWDecode</c> filter.</summary>
/// <remarks>
/// The PDF variant differs from the TIFF one only by <c>EarlyChange</c>, which decides whether the code
/// width grows one code before it strictly needs to. It defaults to 1, and files that set it to 0 and
/// mean it are rare enough that getting this wrong produces convincing-looking garbage.
/// </remarks>
internal static class LzwFilter
{
    private const int ClearCode = 256;
    private const int EndOfDataCode = 257;
    private const int FirstFreeCode = 258;
    private const int MaxCodes = 4096;

    /// <summary>
    /// Decodes <paramref name="data"/>, keeping at most <paramref name="maxLength"/> bytes;
    /// <paramref name="limited"/> says whether it decodes to more.
    /// </summary>
    /// <remarks>
    /// Data that uses a code it has not defined is corrupt from there on: decoding stops, what came before is
    /// kept, and <paramref name="undefinedCode"/> says which code it was. Data that ends without the
    /// end-of-data code is taken as complete, as other readers take it — qpdf among them —, since nothing
    /// tells it from data whose encoder left the code out.
    /// </remarks>
    /// <param name="data">The encoded data.</param>
    /// <param name="earlyChange">1 when the code width grows one code early, as by default; 0 otherwise.</param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="undefinedCode">The code decoding stopped at because the data had not defined it, or -1.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    public static byte[] Decode(
        ReadOnlySpan<byte> data, int earlyChange, out bool limited, out int undefinedCode, int maxLength)
    {
        limited = false;
        undefinedCode = -1;

        var table = new byte[MaxCodes][];
        for (var i = 0; i < 256; i++)
        {
            table[i] = [(byte)i];
        }

        var output = new PdfBoundedOutput(Math.Max(1024, data.Length * 3L), maxLength);
        var next = FirstFreeCode;
        var codeBits = 9;
        byte[]? previous = null;
        var bitBuffer = 0;
        var bitCount = 0;

        foreach (var current in data)
        {
            bitBuffer = (bitBuffer << 8) | current;
            bitCount += 8;

            while (bitCount >= codeBits)
            {
                var code = (bitBuffer >> (bitCount - codeBits)) & ((1 << codeBits) - 1);
                bitCount -= codeBits;
                bitBuffer &= (1 << bitCount) - 1;

                if (code == EndOfDataCode)
                {
                    return output.ToArray();
                }

                if (code == ClearCode)
                {
                    next = FirstFreeCode;
                    codeBits = 9;
                    previous = null;
                    continue;
                }

                byte[] entry;

                if (code < next && table[code] is { } known)
                {
                    entry = known;
                }
                else if (code == next && previous is not null)
                {
                    // The encoder used the code it was defining with this very sequence: the previous one,
                    // followed by its own first byte.
                    entry = [.. previous, previous[0]];
                }
                else
                {
                    // A code the table does not hold, and is not about to: past the next one to be defined, or
                    // the next one with no sequence before it to define it from.
                    undefinedCode = code;
                    return output.ToArray();
                }

                if (!output.TryWrite(entry))
                {
                    limited = true;
                    return output.ToArray();
                }

                if (previous is not null && next < MaxCodes)
                {
                    table[next++] = [.. previous, entry[0]];
                }

                previous = entry;

                if (codeBits < 12 && next + earlyChange >= 1 << codeBits)
                {
                    codeBits++;
                }
            }
        }

        return output.ToArray();
    }
}
