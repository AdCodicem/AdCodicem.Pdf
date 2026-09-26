using System.Buffers;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Bounds every filter respects, so that a hostile file cannot exhaust memory.</summary>
internal static class PdfFilterLimits
{
    /// <summary>
    /// Largest amount of data a single stream may decode to. A compressed stream of a few kilobytes can
    /// expand to gigabytes; refusing to follow it that far is the difference between a diagnostic and a
    /// denial of service.
    /// </summary>
    public const int MaxDecodedLength = 256 * 1024 * 1024;

    /// <summary>
    /// A decoder's first buffer: its own estimate of the output, never more than the output may grow to —
    /// an estimate is a multiple of a length the file chose.
    /// </summary>
    public static int InitialCapacity(long estimate, int maxLength) =>
        (int)Math.Clamp(estimate, 1, Math.Max(1, maxLength));

    /// <summary>
    /// Appends what fits of <paramref name="bytes"/> under <paramref name="maxLength"/>. Returns false when
    /// some of it did not fit, which means the data decodes to more than the bound.
    /// </summary>
    public static bool TryWrite(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> bytes, int maxLength)
    {
        var room = maxLength - output.WrittenCount;

        if (bytes.Length <= room)
        {
            output.Write(bytes);
            return true;
        }

        output.Write(bytes[..Math.Max(room, 0)]);
        return false;
    }
}
