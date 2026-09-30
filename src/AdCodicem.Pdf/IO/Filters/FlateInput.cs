namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// The encoded bytes a Flate decoder reads, which notices when the decoder asks for bytes past their end, and can
/// hand them over one at a time from a given point on.
/// </summary>
/// <remarks>
/// <para>
/// The framework's inflater takes the end of its input for the end of the data: a stream that lost its tail
/// decodes to what is left, and nothing is thrown. But the inflater asks for more input only while the data
/// is unfinished — once it has read the final block and, for zlib, the checksum after it, it asks for
/// nothing more, whatever follows. So a request for bytes past the end is the one sign that the data stopped
/// short, and this stream records it.
/// </para>
/// <para>
/// The inflater throws from the read that meets a fault, and what that read decoded from the input it was last
/// handed is lost with it. Handed that input again one byte at a time, each read decodes what one byte completes,
/// and the read that meets the fault loses no more than what the byte it met the fault in decoded before it. So
/// this stream records where the last bytes it handed over start, and hands the bytes from
/// <paramref name="slowFrom"/> on over one at a time. Before that point it hands them over at most
/// <see cref="ChunkLength"/> at once, so that the bytes from the start of the last piece to a fault are never more.
/// </para>
/// </remarks>
/// <param name="data">The encoded bytes.</param>
/// <param name="slowFrom">Where to start handing the bytes over one at a time; past the end, never.</param>
internal sealed class FlateInput(ReadOnlyMemory<byte> data, int slowFrom = int.MaxValue) : Stream
{
    /// <summary>
    /// The most bytes handed over at once: the input the framework's inflater asks for at once, 8 KB on .NET 10, as
    /// measured.
    /// </summary>
    /// <remarks>
    /// Not a guard (ADR 34): every byte is still handed over, only in pieces of at most this size, so no file is read
    /// differently for it, and the inflater asks for no more on the runtime the library targets, where it changes
    /// nothing. What it bounds is the part of a stream read again a byte at a time after a fault — from the start of
    /// the piece the fault was met in —, which only a stream that faults reaches, and no valid one does: that part
    /// stays one piece long whatever a later runtime's inflater asks for.
    /// </remarks>
    internal const int ChunkLength = 8 * 1024;

    private int _position;

    /// <summary>Gets a value indicating whether the reader asked for bytes when none were left.</summary>
    public bool ReadPastEnd { get; private set; }

    /// <summary>Gets how many bytes the reader was handed.</summary>
    public int BytesRead => _position;

    /// <summary>Gets where the last bytes the reader was handed start.</summary>
    public int LastReadStart { get; private set; }

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

        var count = _position < slowFrom
            ? Math.Min(Math.Min(Math.Min(buffer.Length, left), ChunkLength), slowFrom - _position)
            : 1;
        data.Span.Slice(_position, count).CopyTo(buffer);
        LastReadStart = _position;
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
