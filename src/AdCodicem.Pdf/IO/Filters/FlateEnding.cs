namespace AdCodicem.Pdf.IO.Filters;

/// <summary>How a Flate stream's data ended, as far as decoding it could tell.</summary>
internal enum FlateEnding
{
    /// <summary>
    /// The data ended where its format says it does — or decoding stopped at its bound, before its end could
    /// be seen.
    /// </summary>
    Whole,

    /// <summary>
    /// The data ended after its last block, but before the whole of the zlib checksum that follows it: nothing
    /// was lost, and nothing could be checked.
    /// </summary>
    ChecksumMissing,

    /// <summary>The data ended before its last block: its tail was lost, and what decoded before the end was kept.</summary>
    TailLost,

    /// <summary>The data turned corrupt, and what decoded before the fault was found was kept.</summary>
    Corrupt,
}
