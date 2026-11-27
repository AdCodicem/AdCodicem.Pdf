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
    /// A decode allocates its output and nothing for each code it reads: the code table and the buffer an entry is spelled
    /// into are rented from the shared pool, and returned when the decode ends.
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

        // An entry past the 256 single bytes is kept as the code of the entry it extends and its last byte, which take three
        // bytes where the entry whole takes as many as it denotes, up to 3,839, and an array for each code; it is spelled
        // into the scratch buffer, from its last byte back, each time the data names it.
        var prefixes = ArrayPool<ushort>.Shared.Rent(MaxCodes);
        var lasts = ArrayPool<byte>.Shared.Rent(MaxCodes);
        var scratch = ArrayPool<byte>.Shared.Rent(MaxCodes);

        try
        {
            var output = new PdfBoundedOutput(Math.Max(1024, data.Length * 3L), maxLength);
            var next = FirstFreeCode;
            var codeBits = 9;
            var previous = -1;
            byte previousFirst = 0;
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
                        previous = -1;
                        continue;
                    }

                    int start;

                    if (code < next)
                    {
                        start = Spell(code, prefixes, lasts, scratch, MaxCodes);
                    }
                    else if (code == next && previous >= 0)
                    {
                        // The encoder used the code it was defining with this very sequence: the previous one,
                        // followed by its own first byte.
                        start = Spell(previous, prefixes, lasts, scratch, MaxCodes - 1);
                        scratch[MaxCodes - 1] = previousFirst;
                    }
                    else
                    {
                        // A code the table does not hold, and is not about to: past the next one to be defined, or
                        // the next one with no sequence before it to define it from.
                        undefinedCode = code;
                        return output.ToArray();
                    }

                    if (!output.TryWrite(scratch.AsSpan(start, MaxCodes - start)))
                    {
                        limited = true;
                        return output.ToArray();
                    }

                    var first = scratch[start];

                    if (previous >= 0 && next < MaxCodes)
                    {
                        prefixes[next] = (ushort)previous;
                        lasts[next] = first;
                        next++;
                    }

                    previous = code;
                    previousFirst = first;

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
            ArrayPool<byte>.Shared.Return(scratch);
            ArrayPool<byte>.Shared.Return(lasts);
            ArrayPool<ushort>.Shared.Return(prefixes);
        }
    }

    /// <summary>
    /// Writes the bytes <paramref name="code"/> denotes into <paramref name="scratch"/>, ending before
    /// <paramref name="end"/>, and returns where they start.
    /// </summary>
    /// <remarks>
    /// The walk ends: an entry is defined from the code before it, which the table held or was defining, so the code each
    /// entry extends is less than its own, and the walk reaches a single byte within as many steps as entries were defined
    /// since the last clear code — 3,838 at most, fewer than the scratch buffer holds.
    /// </remarks>
    private static int Spell(int code, ushort[] prefixes, byte[] lasts, byte[] scratch, int end)
    {
        var at = end;

        while (code >= FirstFreeCode)
        {
            scratch[--at] = lasts[code];
            code = prefixes[code];
        }

        scratch[--at] = (byte)code;
        return at;
    }
}
