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
    public static byte[] Decode(ReadOnlyMemory<byte> data, out bool repaired, out bool truncated)
    {
        repaired = false;
        truncated = false;

        if (data.IsEmpty)
        {
            return [];
        }

        if (TryInflate(data, zlibHeader: true, out var result, out truncated))
        {
            return result;
        }

        // A raw deflate stream, or a zlib header that was mangled.
        if (TryInflate(data, zlibHeader: false, out result, out truncated))
        {
            repaired = true;
            return result;
        }

        // Leading white space before the header happens when a generator miscounts /Length.
        var skipped = SkipLeadingWhitespace(data);
        if (skipped > 0 && TryInflate(data[skipped..], zlibHeader: true, out result, out truncated))
        {
            repaired = true;
            return result;
        }

        return [];
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

    private static bool TryInflate(ReadOnlyMemory<byte> data, bool zlibHeader, out byte[] result, out bool truncated)
    {
        truncated = false;

        var input = MemoryMarshal.TryGetArray(data, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);

        using (input)
        {
            using Stream decompressor = zlibHeader
                ? new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true)
                : new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);

            var output = new ArrayBufferWriter<byte>(Math.Max(1024, data.Length * 4));
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
                        truncated = output.WrittenCount > 0;
                        result = output.WrittenSpan.ToArray();
                        return truncated;
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    if (output.WrittenCount + read > PdfFilterLimits.MaxDecodedLength)
                    {
                        truncated = true;
                        result = output.WrittenSpan.ToArray();
                        return true;
                    }

                    output.Write(buffer.AsSpan(0, read));
                }

                result = output.WrittenSpan.ToArray();
                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
