namespace AdCodicem.Pdf.IO.XRef;

/// <summary>
/// What a pass that reads the header of every entry of an index — settling's, or a validation's probe — may still read, and
/// the run of white space it last crossed, which an offset inside it need not read again.
/// </summary>
/// <remarks>
/// A load reads the white space before a header to its end, however far; entries a damaged file places in one long run of
/// it — a block of zeros — would each read it again. What lies past the run decides for any offset inside it, as the lexer
/// skips white space alike from either.
/// </remarks>
/// <param name="allowance">What the pass may read past the first windows of each header.</param>
internal sealed class HeaderReading(long allowance)
{
    /// <summary>Gets or sets what the pass may still read past the first windows of each header.</summary>
    public long Remaining { get; set; } = allowance;

    /// <summary>Gets or sets where the run of white space last crossed starts; it is empty while none was.</summary>
    public long BlankStart { get; set; }

    /// <summary>Gets or sets where that run ends, as far as it was read: its bytes from <see cref="BlankStart"/> are white space.</summary>
    public long BlankEnd { get; set; }

    /// <summary>
    /// Gives where to read on from for <paramref name="offset"/>: the end of the run last crossed when the offset lies inside
    /// it, else the offset.
    /// </summary>
    public long PastBlanks(long offset) => offset >= BlankStart && offset < BlankEnd ? BlankEnd : offset;

    /// <summary>Remembers that the bytes from <paramref name="start"/> to <paramref name="end"/> are white space.</summary>
    /// <remarks>
    /// The run replaces the one remembered unless it continues it: a run only a few bytes long is not worth remembering, and
    /// is not offered.
    /// </remarks>
    public void Crossed(long start, long end)
    {
        if (start >= BlankStart && start <= BlankEnd && BlankEnd > BlankStart)
        {
            BlankEnd = Math.Max(BlankEnd, end);
            return;
        }

        BlankStart = start;
        BlankEnd = end;
    }
}
