namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// The output of one decoding filter: it grows as bytes arrive, never past its bound, and says when some did
/// not fit.
/// </summary>
/// <remarks>
/// The framework's growable buffers double their capacity, so near the bound they hold an array up to twice
/// its size, and a write of nothing into a full one still doubles it. This one stops growing at the bound,
/// and hands its array back without a copy when it is exactly full — as it is for every stream that
/// reaches the bound. A size within a quarter of the bound goes straight to the bound, so that a guess
/// or a doubling that lands just short of it is not followed by a second array of nearly the same size.
/// </remarks>
internal sealed class PdfBoundedOutput
{
    private readonly int _maxLength;
    private byte[] _buffer;
    private int _count;

    /// <summary>Starts with room for <paramref name="estimate"/> bytes, or for the bound if that is less.</summary>
    public PdfBoundedOutput(long estimate, int maxLength)
    {
        _maxLength = Math.Max(maxLength, 0);
        _buffer = new byte[Size(Math.Max(estimate, 0))];
    }

    /// <summary>Gets the number of bytes written.</summary>
    public int Count => _count;

    /// <summary>Gets the size of the array behind the output, which never exceeds the bound.</summary>
    public int Capacity => _buffer.Length;

    /// <summary>Appends what fits of <paramref name="bytes"/>; false when some of it did not.</summary>
    public bool TryWrite(ReadOnlySpan<byte> bytes)
    {
        var fits = Math.Min(bytes.Length, _maxLength - _count);

        if (fits > 0)
        {
            Reserve(fits);
            bytes[..fits].CopyTo(_buffer.AsSpan(_count));
            _count += fits;
        }

        return fits == bytes.Length;
    }

    /// <summary>Appends what fits of <paramref name="count"/> copies of <paramref name="value"/>; false when some did not.</summary>
    public bool TryFill(byte value, int count)
    {
        var fits = Math.Min(count, _maxLength - _count);

        if (fits > 0)
        {
            Reserve(fits);
            _buffer.AsSpan(_count, fits).Fill(value);
            _count += fits;
        }

        return fits == count;
    }

    /// <summary>Returns what was written: the buffer itself when it is exactly full, a copy otherwise.</summary>
    public byte[] ToArray() => _count == _buffer.Length ? _buffer : _buffer.AsSpan(0, _count).ToArray();

    private void Reserve(int more)
    {
        var needed = _count + more;

        if (needed > _buffer.Length)
        {
            Array.Resize(ref _buffer, Size(Math.Max((long)_buffer.Length * 2, needed)));
        }
    }

    private int Size(long wanted) => wanted > _maxLength - (_maxLength / 4) ? _maxLength : (int)wanted;
}
