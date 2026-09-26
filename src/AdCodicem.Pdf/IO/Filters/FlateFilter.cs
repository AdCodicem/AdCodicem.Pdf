using System.Buffers;
using System.IO.Compression;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>FlateDecode</c> filter.</summary>
/// <remarks>
/// The specification says zlib, and most files comply. The ones that do not are common enough to be worth
/// handling: raw deflate with no zlib header, leading white space before the header, and data whose tail
/// was lost. In the last case the bytes that did decode are worth more than an exception — but the loss is
/// said, never left for the caller to find.
/// </remarks>
internal static class FlateFilter
{
    /// <summary>The length of a zlib header without a preset dictionary, which is all a PDF stream can use.</summary>
    private const int ZlibHeaderLength = 2;

    /// <summary>
    /// Decodes a Flate stream, reporting success explicitly.
    /// </summary>
    /// <remarks>
    /// Success is a separate answer from the length of the output. A validly compressed stream that
    /// contains nothing — an empty content stream, an empty appearance — decodes to zero bytes, and
    /// treating that as a failure would hand the caller the compressed bytes instead of the empty
    /// content it asked for.
    /// </remarks>
    /// <param name="data">The encoded data.</param>
    /// <param name="decoded">What was decoded, at most <paramref name="maxLength"/> bytes.</param>
    /// <param name="repaired">Whether the data was not zlib as the specification says, and was read anyway.</param>
    /// <param name="ending">Whether the data ended where its format says it does, and what was kept if not.</param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    public static bool TryDecode(
        ReadOnlyMemory<byte> data,
        out byte[] decoded,
        out bool repaired,
        out FlateEnding ending,
        out bool limited,
        int maxLength)
    {
        repaired = false;
        ending = FlateEnding.Whole;
        limited = false;

        if (data.IsEmpty)
        {
            decoded = [];
            return true;
        }

        if (TryInflate(data, zlibHeader: true, maxLength, out decoded, out ending, out limited))
        {
            return true;
        }

        // A raw deflate stream, or a zlib header that was mangled.
        if (TryInflate(data, zlibHeader: false, maxLength, out decoded, out ending, out limited))
        {
            repaired = true;
            return true;
        }

        // Leading white space before the header happens when a generator miscounts /Length.
        var skipped = SkipLeadingWhitespace(data);
        if (skipped > 0 && TryInflate(data[skipped..], zlibHeader: true, maxLength, out decoded, out ending, out limited))
        {
            repaired = true;
            return true;
        }

        decoded = [];
        ending = FlateEnding.Whole;
        return false;
    }

    private static int SkipLeadingWhitespace(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var index = 0;

        while (index < span.Length && PdfCharacters.IsWhitespace(span[index]))
        {
            index++;
        }

        return index;
    }

    private static bool TryInflate(
        ReadOnlyMemory<byte> data, bool zlibHeader, int maxLength, out byte[] result, out FlateEnding ending, out bool limited)
    {
        ending = FlateEnding.Whole;
        limited = false;

        using var input = new FlateInput(data);
        using Stream decompressor = zlibHeader
            ? new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true)
            : new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);

        var output = new PdfBoundedOutput(Math.Max(1024, data.Length * 4L), maxLength);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            while (true)
            {
                int read;

                try
                {
                    read = decompressor.Read(buffer, 0, buffer.Length);
                }
                catch (InvalidDataException) when (input.ReadPastEnd)
                {
                    // A host that sets System.IO.Compression.UseStrictValidation has the framework throw where
                    // the data stops short. That is the lost tail the input noticed, handled as it is below.
                    break;
                }
                catch (InvalidDataException)
                {
                    // Corrupt from here on. Anything already decoded is still usable, and losing the
                    // tail of a content stream beats losing the whole document.
                    ending = FlateEnding.Corrupt;
                    result = output.ToArray();
                    return output.Count > 0;
                }

                if (read == 0)
                {
                    break;
                }

                if (!output.TryWrite(buffer.AsSpan(0, read)))
                {
                    limited = true;
                    result = output.ToArray();
                    return true;
                }
            }

            // The inflater returns the end of its input as the end of the data. Having asked for more, it had
            // not reached the end of its last block — or, for zlib, of the checksum after it.
            if (input.ReadPastEnd)
            {
                ending = zlibHeader && data.Length > ZlibHeaderLength &&
                    IsWholeDeflate(data[ZlibHeaderLength..], output.Count, buffer)
                    ? FlateEnding.ChecksumMissing
                    : FlateEnding.TailLost;
            }

            result = output.ToArray();
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Determines whether <paramref name="body"/> is deflate data that reaches the end of its last block,
    /// reading it again without keeping what it decodes to.
    /// </summary>
    /// <remarks>
    /// A zlib stream that ran out may have lost its tail, or only its checksum; the framework's inflater does
    /// not say which. Its body read as raw deflate does: raw deflate carries no checksum, so it ends where the
    /// last block does. Only a stream that ran out is read twice, and the second reading stops as soon as it
    /// decodes past the <paramref name="decoded"/> bytes the first one did, which it never does when it is the
    /// same data.
    /// </remarks>
    /// <param name="body">The data after the zlib header.</param>
    /// <param name="decoded">What the zlib reading decoded, which the body must decode to exactly.</param>
    /// <param name="buffer">A buffer to decode into, whose contents are discarded.</param>
    internal static bool IsWholeDeflate(ReadOnlyMemory<byte> body, int decoded, byte[] buffer)
    {
        using var input = new FlateInput(body);
        using var decompressor = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
        long total = 0;

        try
        {
            int read;

            while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;

                if (total > decoded)
                {
                    return false;
                }
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }

        return !input.ReadPastEnd && total == decoded;
    }
}
