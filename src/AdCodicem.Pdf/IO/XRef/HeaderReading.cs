namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// What a pass that reads the header of every entry of an index — settling's, or a validation's probe — may still consume,
/// and the runs of white space it crossed, which an offset inside one need not read again.
/// </summary>
/// <remarks>
/// <para>
/// What a header's reading consumes is the bytes it crossed up to its decision, past the first few of each: a valid file's
/// headers, each at its own offset, are distinct bytes, which consume at most the file's length together.
/// </para>
/// <para>
/// A load reads the white space before a header to its end, however far; entries a damaged file places in a long run of
/// it — a block of zeros — would each read it again. What lies past the run decides for any offset inside it, as the lexer
/// skips white space alike from either, and what the run holds is consumed once. Up to <see cref="MaxRuns"/> runs are
/// remembered, as many blocks as faults may have zeroed, the longest kept when more were crossed.
/// </para>
/// </remarks>
/// <param name="allowance">What the pass may consume past the first bytes of each header.</param>
internal sealed class HeaderReading(long allowance)
{
    /// <summary>
    /// How many runs of white space are remembered at most. Not a bound: what a pass remembers to read less, a run it does
    /// not remember being read again, and consumed again, which <see cref="Remaining"/> bounds.
    /// </summary>
    internal const int MaxRuns = 16;

    /// <summary>
    /// How long a run of white space must be to be remembered on its own: one shorter is crossed within a header's first
    /// window, which costs nothing of the allowance, and would only cut the windows of the readings it lies across.
    /// </summary>
    internal const int ShortestRun = 64;

    private readonly long[] _starts = new long[MaxRuns];
    private readonly long[] _ends = new long[MaxRuns];
    private int _count;

    /// <summary>Gets what the pass may still consume past the first bytes of each header.</summary>
    public long Remaining { get; private set; } = allowance;

    /// <summary>
    /// Gives where to read on from for <paramref name="offset"/>: the end of the run remembered that holds it, else the
    /// offset.
    /// </summary>
    public long PastBlanks(long offset)
    {
        for (var index = 0; index < _count; index++)
        {
            if (offset >= _starts[index] && offset < _ends[index])
            {
                return _ends[index];
            }
        }

        return offset;
    }

    /// <summary>
    /// Gives where the first run remembered past <paramref name="offset"/> starts, or <see cref="long.MaxValue"/> when none
    /// is: a crossing from the offset stops there, and reads on from its end.
    /// </summary>
    public long NextRunAfter(long offset)
    {
        var next = long.MaxValue;

        for (var index = 0; index < _count; index++)
        {
            if (_starts[index] > offset && _starts[index] < next)
            {
                next = _starts[index];
            }
        }

        return next;
    }

    /// <summary>
    /// Takes <paramref name="length"/> bytes from what remains; or, where they are more than remains, takes all that remains.
    /// </summary>
    /// <returns><see langword="true"/> when they were taken; <see langword="false"/> when the pass is spent.</returns>
    public bool Consume(long length)
    {
        if (length > Remaining)
        {
            Remaining = 0;
            return false;
        }

        Remaining -= length;
        return true;
    }

    /// <summary>Remembers that the bytes from <paramref name="start"/> to <paramref name="end"/> are white space.</summary>
    /// <remarks>
    /// A run that meets or overlaps runs remembered joins them, all being white space; another is remembered beside them, if
    /// it is <see cref="ShortestRun"/> bytes long at least — in place of the shortest when <see cref="MaxRuns"/> are, and
    /// only if it is longer.
    /// </remarks>
    public void Crossed(long start, long end)
    {
        var index = 0;

        while (index < _count)
        {
            if (start <= _ends[index] && end >= _starts[index])
            {
                // Joined, the run remembered is taken out, and the union compared with the others again.
                start = Math.Min(start, _starts[index]);
                end = Math.Max(end, _ends[index]);
                _count--;
                _starts[index] = _starts[_count];
                _ends[index] = _ends[_count];
                continue;
            }

            index++;
        }

        if (end - start < ShortestRun)
        {
            return;
        }

        if (_count < MaxRuns)
        {
            _starts[_count] = start;
            _ends[_count] = end;
            _count++;
            return;
        }

        var shortest = 0;

        for (index = 1; index < _count; index++)
        {
            if (_ends[index] - _starts[index] < _ends[shortest] - _starts[shortest])
            {
                shortest = index;
            }
        }

        if (end - start > _ends[shortest] - _starts[shortest])
        {
            _starts[shortest] = start;
            _ends[shortest] = end;
        }
    }
}
