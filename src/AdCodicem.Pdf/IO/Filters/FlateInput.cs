namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// The encoded bytes a Flate decoder reads, which notices when the decoder asks for bytes past their end.
/// </summary>
/// <remarks>
/// The framework's inflater takes the end of its input for the end of the data: a stream that lost its tail
/// decodes to what is left, and nothing is thrown. But the inflater asks for more input only while the data
/// is unfinished — once it has read the final block and, for zlib, the checksum after it, it asks for
/// nothing more, whatever follows. So a request for bytes past the end is the one sign that the data stopped
/// short, and this stream records it.
/// </remarks>
internal sealed class FlateInput(ReadOnlyMemory<byte> data) : Stream
{
    private int _position;

    /// <summary>Gets a value indicating whether the reader asked for bytes when none were left.</summary>
    public bool ReadPastEnd { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        var left = data.Length - _position;

        if (left == 0)
        {
            ReadPastEnd = true;
            return 0;
        }

        var count = Math.Min(buffer.Length, left);
        data.Span.Slice(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
