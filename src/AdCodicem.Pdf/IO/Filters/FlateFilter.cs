using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>FlateDecode</c> filter.</summary>
/// <remarks>
/// The specification says zlib, and most files comply. The ones that do not are common enough to be worth
/// handling: raw deflate with no zlib header, leading white space before the header, and data whose tail
/// was lost. In the last case the bytes that did decode are worth more than an exception.
/// </remarks>
internal static class FlateFilter
{
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
    /// <param name="truncated">Whether the data turned corrupt part-way, and only what came before was kept.</param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    public static bool TryDecode(
        ReadOnlyMemory<byte> data,
        out byte[] decoded,
        out bool repaired,
        out bool truncated,
        out bool limited,
        int maxLength)
    {
        repaired = false;
        truncated = false;
        limited = false;

        if (data.IsEmpty)
        {
            decoded = [];
            return true;
        }

        if (TryInflate(data, zlibHeader: true, maxLength, out decoded, out truncated, out limited))
        {
            return true;
        }

        // A raw deflate stream, or a zlib header that was mangled.
        if (TryInflate(data, zlibHeader: false, maxLength, out decoded, out truncated, out limited))
        {
            repaired = true;
            return true;
        }

        // Leading white space before the header happens when a generator miscounts /Length.
        var skipped = SkipLeadingWhitespace(data);
        if (skipped > 0 && TryInflate(data[skipped..], zlibHeader: true, maxLength, out decoded, out truncated, out limited))
        {
            repaired = true;
            return true;
        }

        decoded = [];
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
        ReadOnlyMemory<byte> data, bool zlibHeader, int maxLength, out byte[] result, out bool truncated, out bool limited)
    {
        truncated = false;
        limited = false;

        var input = MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);

        using (input)
        {
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
                    catch (InvalidDataException)
                    {
                        // Corrupt from here on. Anything already decoded is still usable, and losing the
                        // tail of a content stream beats losing the whole document.
                        truncated = output.Count > 0;
                        result = output.ToArray();
                        return truncated;
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

                result = output.ToArray();
                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
