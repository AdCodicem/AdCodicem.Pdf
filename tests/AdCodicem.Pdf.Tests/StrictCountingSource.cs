using AdCodicem.Pdf.IO;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A source that counts what the reader reads from it, and refuses any read that starts or runs past the
/// end of its data: the strictest a third-party <see cref="PdfFileSource"/> can be, since the reader only
/// ever asks for bytes the file has.
/// </summary>
internal sealed class StrictCountingSource(byte[] data) : PdfFileSource
{
    /// <summary>Gets the number of bytes read so far.</summary>
    public long BytesRead { get; private set; }

    /// <inheritdoc/>
    public override long Length => data.Length;

    /// <inheritdoc/>
    public override int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > data.Length)
        {
            throw new InvalidOperationException(
                $"The reader asked for {buffer.Length} bytes at {offset} of a {data.Length}-byte source.");
        }

        data.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
        BytesRead += buffer.Length;
        return buffer.Length;
    }
}
