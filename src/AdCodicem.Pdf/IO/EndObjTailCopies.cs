namespace AdCodicem.Pdf.IO;

/// <summary>
/// The <see cref="EndObjTail"/> of a search for <c>endstream</c> as it stood at each place where a number carried into the
/// next read starts (<see cref="HeaderText.CarriedStarts"/>): where a header a later read finds may start before that read
/// (#261).
/// </summary>
/// <remarks>
/// A header's tokens may have comment lines between them, and a comment may hold <c>endobj</c>: the reads before the one
/// that decides the header read past its start, and their tail would have taken that <c>endobj</c> for the object's last,
/// and seen one, according to where the reads part the data. A copy of the tail is taken at each place, as the reads reach
/// it, and kept as long as the reads after carry a number that starts there: six at most, the numbers a read carries, and
/// each copy a few fields, whatever the data holds. No byte is read twice.
/// </remarks>
internal ref struct EndObjTailCopies
{
    private readonly Span<EndObjTail> _copies;
    private readonly Span<long> _at;
    private int _count;

    /// <summary>Initializes a new instance of the <see cref="EndObjTailCopies"/> struct, which holds no copy.</summary>
    /// <param name="copies">Room for <see cref="HeaderText.MostNumbersCarried"/> copies.</param>
    /// <param name="at">Room for where each was taken.</param>
    public EndObjTailCopies(Span<EndObjTail> copies, Span<long> at)
    {
        _copies = copies;
        _at = at;
    }

    /// <summary>
    /// Reads into <paramref name="tail"/> the bytes of a read from where it stands to <paramref name="until"/>, where the next
    /// read starts, taking a copy of it at each of <paramref name="starts"/> it reaches, and keeps the copies taken before at
    /// the places <paramref name="starts"/> still holds, dropping the others.
    /// </summary>
    /// <param name="tail">The tail the search follows.</param>
    /// <param name="bytes">The bytes of the read.</param>
    /// <param name="at">Where they start in the file.</param>
    /// <param name="until">Where the next read starts.</param>
    /// <param name="starts">
    /// Where the numbers carried into the next read start, each once, the nearest the start of the data first; those before
    /// where the tail stands were carried into this read too.
    /// </param>
    public void ReadTo(ref EndObjTail tail, ReadOnlySpan<byte> bytes, long at, long until, ReadOnlySpan<long> starts)
    {
        var kept = 0;
        var old = 0;

        // Both lists run from the start of the data, and the copies kept never outrun those looked at, so they are written
        // over in place: once a place the tail has not reached comes, none it has reached follows.
        foreach (var start in starts)
        {
            if (start < tail.UpTo)
            {
                while (old < _count && _at[old] < start)
                {
                    old++;
                }

                if (old < _count && _at[old] == start)
                {
                    _copies[kept] = _copies[old];
                    _at[kept] = start;
                    kept++;
                    old++;
                }
            }
            else
            {
                // A place the tail has not reached lies in these bytes: every number carried into the next read starts before
                // it.
                tail.Read(bytes, at, start);
                _copies[kept] = tail;
                _at[kept] = start;
                kept++;
            }
        }

        _count = kept;
        tail.Read(bytes, at, until);
    }

    /// <summary>Gives the tail as it stood at <paramref name="start"/>, where a copy was kept, and says whether one was.</summary>
    public readonly bool TryGet(long start, out EndObjTail tail)
    {
        for (var index = 0; index < _count; index++)
        {
            if (_at[index] == start)
            {
                tail = _copies[index];
                return true;
            }
        }

        tail = default;
        return false;
    }
}
