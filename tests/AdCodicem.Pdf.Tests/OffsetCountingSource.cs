using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>A file in memory that counts what is read of it, and how many reads start at each offset.</summary>
internal sealed class OffsetCountingSource(byte[] data) : PdfFileSource
{
    /// <summary>Gets how many reads started at each offset.</summary>
    public Dictionary<long, int> Reads { get; } = [];

    /// <summary>Gets the number of bytes read so far.</summary>
    public long BytesRead { get; private set; }

    /// <inheritdoc/>
    public override long Length => data.Length;

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        var count = (int)Math.Min(buffer.Length, data.Length - offset);
        data.AsSpan((int)offset, count).CopyTo(buffer);
        Reads[offset] = Reads.GetValueOrDefault(offset) + 1;
        BytesRead += count;
        return count;
    }
}
