using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A source over a file that another process cuts short while a document is open on it: its length stays the one it
/// had when the document opened, and a read reaching past the cut returns only the bytes before it, as a file source
/// reading a file that has shrunk does.
/// </summary>
internal sealed class CutShortSource(byte[] data) : PdfFileSource
{
    private int _available = data.Length;
    private long _cutWhenReadAt = -1;
    private int _cutTo;

    /// <inheritdoc/>
    public override long Length => data.Length;

    /// <summary>Cuts the file at <paramref name="length"/> bytes, the length the source gives unchanged.</summary>
    public void CutAt(int length) => _available = length;

    /// <summary>
    /// Cuts the file at <paramref name="length"/> bytes when the reader first reads at <paramref name="position"/>, that read
    /// included: a file cut short while the document opens, past what the reader read before.
    /// </summary>
    public void CutWhenReadAt(long position, int length) => (_cutWhenReadAt, _cutTo) = (position, length);

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        if (offset == _cutWhenReadAt)
        {
            (_available, _cutWhenReadAt) = (_cutTo, -1);
        }

        if (offset < 0 || offset >= _available)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, _available - offset);
        data.AsSpan((int)offset, count).CopyTo(buffer);
        return count;
    }
}
