using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A source that gives at most a set number of bytes a read, whatever it is asked for, as a caller's source over a store
/// of blocks may: <see cref="PdfFileSource.Read"/> returns the count read, which may fall short of what was asked before
/// the end of the file, and the reader takes the bytes it is given as all the file holds there (#52).
/// </summary>
internal sealed class CappedReadSource(byte[] data, int cap) : PdfFileSource
{
    /// <inheritdoc/>
    public override long Length => data.Length;

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= data.Length)
        {
            return 0;
        }

        var count = (int)Math.Min(Math.Min(buffer.Length, cap), data.Length - offset);
        data.AsSpan((int)offset, count).CopyTo(buffer);
        return count;
    }
}
