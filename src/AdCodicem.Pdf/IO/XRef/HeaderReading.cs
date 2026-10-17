namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// What a pass that reads the header of every entry of an index — settling's, or a validation's probe — may still consume,
/// and the run of white space it crossed, which an offset inside it need not read again.
/// </summary>
/// <remarks>
/// <para>
/// What a header's reading consumes is the bytes it crossed up to its decision, past the first few of each: a valid file's
/// headers, each at its own offset, are distinct bytes, which consume at most the file's length together.
/// </para>
/// <para>
/// A load reads the white space before a header to its end, however far; entries a damaged file places in one long run of
/// it — a block of zeros — would each read it again. What lies past the run decides for any offset inside it, as the lexer
/// skips white space alike from either, and what the run holds is consumed once.
/// </para>
/// </remarks>
/// <param name="allowance">What the pass may consume past the first bytes of each header.</param>
internal sealed class HeaderReading(long allowance)
{
    /// <summary>Gets what the pass may still consume past the first bytes of each header.</summary>
    public long Remaining { get; private set; } = allowance;

    /// <summary>Gets where the run of white space remembered starts; it is empty while none was crossed.</summary>
    public long BlankStart { get; private set; }

    /// <summary>Gets where that run ends, as far as it was read: its bytes from <see cref="BlankStart"/> are white space.</summary>
    public long BlankEnd { get; private set; }

    /// <summary>
    /// Gives where to read on from for <paramref name="offset"/>: the end of the run remembered when the offset lies inside
    /// it, else the offset.
    /// </summary>
    public long PastBlanks(long offset) => offset >= BlankStart && offset < BlankEnd ? BlankEnd : offset;

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
    /// A run that meets or overlaps the one remembered joins it, both being white space; another replaces it when longer,
    /// so that a long run is not forgotten for a short one crossed between two of its entries.
    /// </remarks>
    public void Crossed(long start, long end)
    {
        if (start <= BlankEnd && end >= BlankStart && BlankEnd > BlankStart)
        {
            BlankStart = Math.Min(BlankStart, start);
            BlankEnd = Math.Max(BlankEnd, end);
        }
        else if (end - start > BlankEnd - BlankStart)
        {
            BlankStart = start;
            BlankEnd = end;
        }
    }
}
