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

    /// <inheritdoc/>
    public override long Length => data.Length;

    /// <summary>Cuts the file at <paramref name="length"/> bytes, the length the source gives unchanged.</summary>
    public void CutAt(int length) => _available = length;

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= _available)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, _available - offset);
        data.AsSpan((int)offset, count).CopyTo(buffer);
        return count;
    }
}
