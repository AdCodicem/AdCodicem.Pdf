using System.Buffers;

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
    public static byte[] Decode(
        ReadOnlySpan<byte> data, int earlyChange, out bool limited, int maxLength = PdfFilterLimits.MaxDecodedLength)
    {
        limited = false;

        var table = new byte[MaxCodes][];
        for (var i = 0; i < 256; i++)
        {
            table[i] = [(byte)i];
        }

        var output = new ArrayBufferWriter<byte>(PdfFilterLimits.InitialCapacity(Math.Max(1024, data.Length * 3L), maxLength));
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
                    return output.WrittenSpan.ToArray();
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
                else if (previous is not null)
                {
                    // The encoder used a code it defined in the very sequence being decoded.
                    entry = [.. previous, previous[0]];
                }
                else
                {
                    return output.WrittenSpan.ToArray();
                }

                if (!PdfFilterLimits.TryWrite(output, entry, maxLength))
                {
                    limited = true;
                    return output.WrittenSpan.ToArray();
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

        return output.WrittenSpan.ToArray();
    }
}
