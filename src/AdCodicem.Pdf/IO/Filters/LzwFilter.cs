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

    /// <summary>
    /// The size of the code table. Not a bound a file chooses: LZW codes are at most 12 bits wide (ISO 32000-1,
    /// 7.4.4.2), so the table holds every code a stream can name.
    /// </summary>
    private const int MaxCodes = 4096;

    /// <summary>
    /// Decodes <paramref name="data"/>, keeping at most <paramref name="maxLength"/> bytes;
    /// <paramref name="limited"/> says whether it decodes to more.
    /// </summary>
    /// <remarks>
    /// Data that uses a code it has not defined is corrupt from there on: decoding stops, what came before is
    /// kept, and <paramref name="undefinedCode"/> says which code it was. Data that ends without the
    /// end-of-data code decodes to its end, as other readers decode it — qpdf among them —, and
    /// <paramref name="ending"/> says so, and whether it ends inside a code: on a whole byte past the last code, or on
    /// bits a padding to the byte would leave zero.
    /// <para>
    /// A decode allocates its output and nothing for each code it reads: the code table is rented from the shared pool, and
    /// returned when the decode ends.
    /// </para>
    /// </remarks>
    /// <param name="data">The encoded data.</param>
    /// <param name="earlyChange">1 when the code width grows one code early, as by default; 0 otherwise.</param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="undefinedCode">The code decoding stopped at because the data had not defined it, or -1.</param>
    /// <param name="ending">How the data ended, when it ran out before its end-of-data code.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    public static byte[] Decode(
        ReadOnlySpan<byte> data, int earlyChange, out bool limited, out int undefinedCode, out DataEnding ending, int maxLength)
    {
        limited = false;
        undefinedCode = -1;
        ending = DataEnding.Marked;

        // An entry past the 256 single bytes is the entry written before it and the first byte written after that one, so
        // it lies whole in what the output holds already: the table keeps where, and how long, and an entry the data names
        // again is copied from there. Kept whole, an entry would take as many bytes as it denotes, up to 3,839, and an
        // array for each code; spelled back through the codes it extends, a long entry would cost a read for each byte.
        var starts = ArrayPool<int>.Shared.Rent(MaxCodes);
        var lengths = ArrayPool<ushort>.Shared.Rent(MaxCodes);

        try
        {
            var output = new PdfBoundedOutput(Math.Max(1024, data.Length * 3L), maxLength);
            var next = FirstFreeCode;
            var codeBits = 9;
            var previousStart = 0;
            var previousLength = 0;
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
                        previousLength = 0;
                        continue;
                    }

                    var start = output.Count;
                    int length;
                    bool whole;

                    if (code < ClearCode)
                    {
                        length = 1;
                        whole = output.TryFill((byte)code, 1);
                    }
                    else if (code < next)
                    {
                        length = lengths[code];
                        whole = output.TryRepeat(starts[code], length);
                    }
                    else if (code == next && previousLength > 0)
                    {
                        // The encoder used the code it was defining with this very sequence: the previous one,
                        // followed by its own first byte.
                        length = previousLength + 1;
                        whole = output.TryRepeat(previousStart, previousLength) && output.TryRepeat(previousStart, 1);
                    }
                    else
                    {
                        // A code the table does not hold, and is not about to: past the next one to be defined, or
                        // the next one with no sequence before it to define it from.
                        undefinedCode = code;
                        return output.ToArray();
                    }

                    if (!whole)
                    {
                        limited = true;
                        return output.ToArray();
                    }

                    if (previousLength > 0 && next < MaxCodes)
                    {
                        starts[next] = previousStart;
                        lengths[next] = (ushort)(previousLength + 1);
                        next++;
                    }

                    previousStart = start;
                    previousLength = length;

                    if (codeBits < 12 && next + earlyChange >= 1 << codeBits)
                    {
                        codeBits++;
                    }
                }
            }

            // What is left is fewer bits than a code: an encoder pads the last code to the byte with zero bits, so a whole
            // byte, or a bit that is set, is part of a code the data lost.
            ending = bitCount >= 8 || bitBuffer != 0 ? DataEnding.UnmarkedInsideCode : DataEnding.Unmarked;
            return output.ToArray();
        }
        finally
        {
            ArrayPool<ushort>.Shared.Return(lengths);
            ArrayPool<int>.Shared.Return(starts);
        }
    }
}
