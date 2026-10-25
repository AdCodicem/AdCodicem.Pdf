namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// What a filter met in its data that its format does not allow, besides what it decoded: bytes it passed over, an end
/// marker broken or missing. The filter fills it as it reads, and the pipeline reports it.
/// </summary>
/// <remarks>
/// Each place is counted from 1, in the data the filter was given, as Flate's reports count theirs. Counting costs the
/// filter nothing it allocates: a fault is reported once for each kind and step, however often the data repeats it.
/// </remarks>
internal struct DataFaults
{
    /// <summary>How many bytes encode nothing where they stand, and were passed over.</summary>
    public int Passed;

    /// <summary>Where the first byte passed over lies; 0 when there is none.</summary>
    public int FirstPassed;

    /// <summary>How many ASCII85 groups have a value that does not fit in four bytes.</summary>
    public int Overflowing;

    /// <summary>Where the first of those groups ends; 0 when there is none.</summary>
    public int FirstOverflowing;

    /// <summary>Where an ASCII85 <c>~</c> that something other than <c>&gt;</c> follows lies; 0 when there is none.</summary>
    public int BrokenMarker;

    /// <summary>Whether ASCII85 data ends on a <c>~</c> that no <c>&gt;</c> follows.</summary>
    public bool UnfinishedMarker;

    /// <summary>Whether ASCII85 data starts with the <c>&lt;~</c> PostScript writes, which was passed over.</summary>
    public bool Introducer;

    /// <summary>Whether the last ASCII85 group before the end marker holds a single digit, which was dropped.</summary>
    public bool LoneDigit;

    /// <summary>How the data ended.</summary>
    public DataEnding Ending;

    /// <summary>
    /// For data that ends inside a unit: how many digits of an ASCII85 group it holds, or how many bytes a RunLength
    /// run declares.
    /// </summary>
    public int UnitLength;

    /// <summary>For data that ends inside a RunLength run: how many of its bytes it holds.</summary>
    public int UnitHeld;

    /// <summary>
    /// For data the reader cut, or whose end the caller says a guard hides, that ends inside an ASCII85 group or an
    /// ASCIIHex pair: how many bytes the unit would decode to, which were not written.
    /// </summary>
    public int Dropped;

    /// <summary>Counts a byte passed over at <paramref name="index"/>, counted from 0.</summary>
    public void Pass(int index)
    {
        if (Passed++ == 0)
        {
            FirstPassed = index + 1;
        }
    }

    /// <summary>Counts an ASCII85 group whose value does not fit in four bytes, ending at <paramref name="index"/>, counted from 0.</summary>
    public void Overflow(int index)
    {
        if (Overflowing++ == 0)
        {
            FirstOverflowing = index + 1;
        }
    }
}
